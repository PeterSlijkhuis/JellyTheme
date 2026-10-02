using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _previews = new();

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
    /// Whether <paramref name="item"/> can hold a theme and has none yet.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>True if the theme is missing.</returns>
    public static bool MissingTheme(BaseItem item)
    {
        var folder = ThemeFolder(item);
        try
        {
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !ThemeFiles.HasTheme(folder);
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
    public static bool NeedsTheme(BaseItem item)
        => (item.TryGetProviderId(MetadataProvider.Tmdb, out _) || (item is Series && item.TryGetProviderId(MetadataProvider.Tvdb, out _)))
           && MissingTheme(item);

    /// <summary>
    /// Saves a theme for <paramref name="item"/> and queues a refresh so it plays right away.
    /// </summary>
    /// <param name="item">The movie or series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a theme was saved.</returns>
    public async Task<bool> TrySaveAsync(BaseItem item, CancellationToken cancellationToken)
    {
        if (!NeedsTheme(item))
        {
            return false;
        }

        var folder = ThemeFolder(item)!;
        var http = _httpClientFactory.CreateClient();
        string? source = null;

        // Each source gets its own try, so a Plex outage still falls through to ThemerrDB.
        if (item is Series && item.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId)
            && await TryAsync(item, "Plex", () => PlexThemeDownloader.TryDownloadAsync(http, tvdbId, folder, cancellationToken)).ConfigureAwait(false))
        {
            source = "Plex";
        }
        else if (item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId)
                 && await TryAsync(item, "ThemerrDB", async () => await ThemerrDb.GetYouTubeUrlAsync(http, ThemerrKind(item), tmdbId, cancellationToken).ConfigureAwait(false) is { } url
                                                              && await YouTubeAudio.TrySaveAsync(http, url, folder, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false))
        {
            source = "ThemerrDB";
        }

        if (source is null)
        {
            return false;
        }

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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if saved, false if the item can't take a theme or the video has no usable audio.</returns>
    public async Task<bool> SaveYouTubeAsync(BaseItem item, VideoId videoId, CancellationToken cancellationToken)
    {
        if (!MissingTheme(item) || await CachePreviewAsync(videoId, cancellationToken).ConfigureAwait(false) is not { } cached)
        {
            return false;
        }

        await using (var audio = File.OpenRead(cached))
        {
            await ThemeFiles.SaveAsync(audio, ThemeFolder(item)!, "theme.m4a", cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> TryAsync(BaseItem item, string source, Func<Task<bool>> save)
    {
        try
        {
            return await save().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or YoutubeExplodeException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Could not save {Source} theme for {Name}", source, item.Name);
            return false;
        }
    }

    private void QueueRefresh(BaseItem item)
        => _providerManager.QueueRefresh(item.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.Normal);
}
