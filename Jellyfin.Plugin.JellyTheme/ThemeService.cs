using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Finds and saves a theme for one movie or show, then tells Jellyfin to pick it up.
/// Sources in order: Plex (TV, by TVDB id), then ThemerrDB (movies, TV and collections, by TMDB id).
/// </summary>
public class ThemeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<ThemeService> _logger;
    private static readonly TimeSpan MissRetry = TimeSpan.FromDays(7);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _previews = new();
    private readonly Lock _missLock = new();
    private Dictionary<Guid, DateTime>? _misses;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Http client factory.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="fileSystem">File system.</param>
    /// <param name="applicationPaths">Application paths, for the preview cache.</param>
    /// <param name="logger">Logger.</param>
    public ThemeService(IHttpClientFactory httpClientFactory, IProviderManager providerManager, IFileSystem fileSystem, IApplicationPaths applicationPaths, ILogger<ThemeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <summary>
    /// The folder a theme goes in: the show or collection folder, or the movie's own folder.
    /// Movies sharing a folder with other movies get none, since the theme would play for all of them.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>The folder, or null.</returns>
    public static string? ThemeFolder(BaseItem item) => item switch
    {
        Series or BoxSet => item.Path,
        Movie when !item.IsInMixedFolder => item.ContainingFolderPath,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="item"/> has a folder a theme can go in.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>True if a theme can be saved for it.</returns>
    public static bool CanHoldTheme(BaseItem item)
    {
        var folder = ThemeFolder(item);
        return !string.IsNullOrEmpty(folder) && Directory.Exists(folder);
    }

    /// <summary>
    /// Whether <paramref name="item"/> can hold a theme but has none.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>True if missing.</returns>
    public static bool MissingTheme(BaseItem item)
    {
        try
        {
            return CanHoldTheme(item) && !ThemeFiles.HasTheme(ThemeFolder(item)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Runs inside Jellyfin's ItemUpdated event; an unreadable folder must never throw into it.
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="item"/> has no theme yet and an id some source can look up.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>True if worth looking up.</returns>
    public static bool NeedsTheme(BaseItem item) => HasLookupId(item) && MissingTheme(item);

    /// <summary>
    /// Whether <paramref name="item"/> has an id Plex or ThemerrDB can look up. Doesn't touch the disk.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>True if it has a TMDB id, or a TVDB id for a show.</returns>
    public static bool HasLookupId(BaseItem item)
        => item.TryGetProviderId(MetadataProvider.Tmdb, out _) || (item is Series && item.TryGetProviderId(MetadataProvider.Tvdb, out _));

    /// <summary>
    /// Saves a theme for <paramref name="item"/> and queues a refresh so it plays right away.
    /// </summary>
    /// <param name="item">The movie or series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a theme was saved.</returns>
    public async Task<bool> TrySaveAsync(BaseItem item, CancellationToken cancellationToken)
    {
        if (!NeedsTheme(item) || RecentlyMissed(item))
        {
            return false;
        }

        var folder = ThemeFolder(item)!;
        var http = _httpClientFactory.CreateClient();

        // Each source gets its own try, so a Plex outage still falls through to ThemerrDB. Null means the source failed.
        bool? plex = item is Series && item.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId)
            ? await TryAsync(item, "Plex", () => PlexThemeDownloader.TryDownloadAsync(http, tvdbId, folder, cancellationToken)).ConfigureAwait(false)
            : false;
        bool? themerr = plex is not true && item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId)
            ? await TryAsync(item, "ThemerrDB", async () => await ThemerrDb.GetYouTubeUrlAsync(http, ThemerrKind(item), tmdbId, cancellationToken).ConfigureAwait(false) is { } url
                                                       && await YouTubeAudio.TrySaveAsync(http, url, folder, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false)
            : false;

        var source = plex is true ? "Plex" : themerr is true ? "ThemerrDB" : null;
        if (source is null)
        {
            // Only a clean "no theme anywhere" is remembered; after an outage the item is tried again next time.
            if (plex is not null && themerr is not null)
            {
                SetMissed(item.Id, true);
            }

            return false;
        }

        SetMissed(item.Id, false);
        _logger.LogInformation("Saved {Source} theme for {Name}", source, item.Name);
        QueueRefresh(item);
        return true;
    }

    /// <summary>
    /// Which ThemerrDB section an item's TMDB id points into. A collection's TMDB id is its TMDB collection id.
    /// </summary>
    /// <param name="item">Movie, show or collection.</param>
    /// <returns>The section.</returns>
    public static ThemerrDb.Kind ThemerrKind(BaseItem item)
        => item switch { Series => ThemerrDb.Kind.Show, BoxSet => ThemerrDb.Kind.Collection, _ => ThemerrDb.Kind.Movie };

    private string PreviewCache => Path.Combine(_applicationPaths.CachePath, "jellytheme");

    private string? MissFile => string.IsNullOrEmpty(_applicationPaths.DataPath) ? null : Path.Combine(_applicationPaths.DataPath, "jellytheme", "misses.json");

    /// <summary>
    /// Downloads (once) the audio of a YouTube video for previewing.
    /// </summary>
    /// <param name="videoId">YouTube video id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path of the cached audio, or null if the video has no usable audio.</returns>
    public Task<string?> CachePreviewAsync(VideoId videoId, CancellationToken cancellationToken)
    {
        // One shared download per video: a prefetch, a preview and a save all wait on the same one,
        // and it keeps going when one of them gives up, so the next click finds it done.
        var key = videoId.Value;
        var download = _previews.GetOrAdd(key, _ => new Lazy<Task<string?>>(() =>
            YouTubeAudio.CacheAsync(_httpClientFactory.CreateClient(), videoId, PreviewCache, CancellationToken.None)));
        var task = download.Value;
        task.ContinueWith(_ => _previews.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(key, download)), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Removes old previews. Runs with the daily task, so the cache empties even when nobody searches.
    /// </summary>
    public void CleanPreviewCache() => YouTubeAudio.CleanCache(PreviewCache);

    /// <summary>
    /// Starts caching previews in the background, so pressing play on a search result starts at once.
    /// </summary>
    /// <param name="videoIds">The search results.</param>
    public void PrefetchPreviews(IEnumerable<VideoId> videoIds)
    {
        foreach (var id in videoIds)
        {
            CachePreviewAsync(id, CancellationToken.None).ContinueWith(
                t => _logger.LogDebug(t.Exception, "Prefetching preview {VideoId} failed", id),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Saves the audio of a YouTube video the user picked as the theme for <paramref name="item"/>.
    /// Reuses the preview download when there is one.
    /// </summary>
    /// <param name="item">The movie or series.</param>
    /// <param name="videoId">YouTube video id.</param>
    /// <param name="replace">Replace an existing theme; the old theme.* files are kept as hidden backups.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if saved, false if the item can't take a theme or the video has no usable audio.</returns>
    public async Task<bool> SaveYouTubeAsync(BaseItem item, VideoId videoId, bool replace, CancellationToken cancellationToken)
    {
        if (!(replace ? CanHoldTheme(item) : MissingTheme(item)) || await CachePreviewAsync(videoId, cancellationToken).ConfigureAwait(false) is not { } cached)
        {
            return false;
        }

        var folder = ThemeFolder(item)!;
        if (replace)
        {
            ThemeFiles.SetAside(folder);
        }

        try
        {
            await using var audio = File.OpenRead(cached);
            await ThemeFiles.SaveAsync(audio, folder, "theme.m4a", cancellationToken).ConfigureAwait(false);
        }
        catch when (replace)
        {
            ThemeFiles.Restore(folder);
            throw;
        }

        // The theme now lives in the item's folder; the cached copy is no longer needed.
        try
        {
            File.Delete(cached);
        }
        catch (IOException)
        {
            // Still being streamed to a preview; the cache cleanup removes it later.
        }

        _logger.LogInformation("Saved picked YouTube theme {VideoId} for {Name}", videoId, item.Name);
        QueueRefresh(item);
        return true;
    }

    private async Task<bool?> TryAsync(BaseItem item, string source, Func<Task<bool>> save)
    {
        try
        {
            return await save().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or YoutubeExplodeException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Could not save {Source} theme for {Name}", source, item.Name);
            return null;
        }
    }

    /// <summary>
    /// Whether no source had a theme for <paramref name="item"/> in the last week. Remembered across restarts,
    /// so the daily task and every server start don't ask Plex and ThemerrDB again for the same misses.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>True if it was missed recently.</returns>
    public bool RecentlyMissed(BaseItem item)
    {
        lock (_missLock)
        {
            return Misses().TryGetValue(item.Id, out var at) && DateTime.UtcNow - at < MissRetry;
        }
    }

    private void SetMissed(Guid id, bool missed)
    {
        lock (_missLock)
        {
            var misses = Misses();
            if (missed)
            {
                misses[id] = DateTime.UtcNow;
            }
            else if (!misses.Remove(id))
            {
                return;
            }

            if (MissFile is { } file)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(misses));
                    File.Move(file + ".tmp", file, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not remember the themes no source had");
                }
            }
        }
    }

    private Dictionary<Guid, DateTime> Misses()
    {
        if (_misses is null)
        {
            try
            {
                _misses = MissFile is { } file && File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<Guid, DateTime>>(File.ReadAllText(file)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A broken file only means asking again.
            }

            _misses ??= [];
        }

        return _misses;
    }

    // Validation only: re-reads the item's folder so Jellyfin sees the new theme file,
    // without asking TMDb or other providers for metadata again.
    private void QueueRefresh(BaseItem item)
        => _providerManager.QueueRefresh(
            item.Id,
            new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
            },
            RefreshPriority.Normal);
}
