using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using YoutubeExplode.Exceptions;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Finds and saves a theme for one movie or show, then tells Jellyfin to pick it up.
/// Sources in order: Plex (TV, by TVDB id), then ThemerrDB (movies and TV, by TMDB id).
/// </summary>
public class ThemeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ThemeService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Http client factory.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="fileSystem">File system.</param>
    /// <param name="logger">Logger.</param>
    public ThemeService(IHttpClientFactory httpClientFactory, IProviderManager providerManager, IFileSystem fileSystem, ILogger<ThemeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// The folder a theme goes in: the show folder, or the movie's own folder.
    /// Movies sharing a folder with other movies get none, since the theme would play for all of them.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>The folder, or null.</returns>
    public static string? ThemeFolder(BaseItem item) => item switch
    {
        Series => item.Path,
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
        return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !ThemeFiles.HasTheme(folder);
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
        try
        {
            if (item is Series && item.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId)
                && await PlexThemeDownloader.TryDownloadAsync(http, tvdbId, folder, cancellationToken).ConfigureAwait(false))
            {
                source = "Plex";
            }
            else if (item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId)
                     && await ThemerrDb.GetYouTubeUrlAsync(http, item is Movie, tmdbId, cancellationToken).ConfigureAwait(false) is { } url
                     && await YouTubeAudio.TrySaveAsync(http, url, folder, cancellationToken).ConfigureAwait(false))
            {
                source = "ThemerrDB";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or YoutubeExplodeException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Could not save theme for {Name}", item.Name);
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
    /// Saves the audio of a YouTube video the user picked as the theme for <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The movie or series.</param>
    /// <param name="videoId">YouTube video id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if saved, false if the item can't take a theme or the video has no usable audio.</returns>
    public async Task<bool> SaveYouTubeAsync(BaseItem item, string videoId, CancellationToken cancellationToken)
    {
        if (!MissingTheme(item)
            || !await YouTubeAudio.TrySaveAsync(_httpClientFactory.CreateClient(), videoId, ThemeFolder(item)!, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        _logger.LogInformation("Saved picked YouTube theme {VideoId} for {Name}", videoId, item.Name);
        QueueRefresh(item);
        return true;
    }

    private void QueueRefresh(BaseItem item)
        => _providerManager.QueueRefresh(item.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.Normal);
}
