using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Saves a Plex theme for one show and tells Jellyfin to pick it up.
/// </summary>
public class PlexThemeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<PlexThemeService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexThemeService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Http client factory.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="fileSystem">File system.</param>
    /// <param name="logger">Logger.</param>
    public PlexThemeService(IHttpClientFactory httpClientFactory, IProviderManager providerManager, IFileSystem fileSystem, ILogger<PlexThemeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Whether <paramref name="item"/> is a show on disk with a TVDB id and no theme yet.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>True if a Plex theme could be saved for it.</returns>
    public static bool NeedsTheme(BaseItem item)
        => item is Series
           && !string.IsNullOrEmpty(item.Path)
           && item.TryGetProviderId(MetadataProvider.Tvdb, out _)
           && !PlexThemeDownloader.HasTheme(item.Path);

    /// <summary>
    /// Saves the Plex theme for <paramref name="show"/> and queues a refresh so it plays right away.
    /// </summary>
    /// <param name="show">The series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a theme was saved.</returns>
    public async Task<bool> TrySaveAsync(BaseItem show, CancellationToken cancellationToken)
    {
        if (!NeedsTheme(show))
        {
            return false;
        }

        try
        {
            if (!await PlexThemeDownloader.TryDownloadAsync(_httpClientFactory.CreateClient(), show.GetProviderId(MetadataProvider.Tvdb)!, show.Path, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save Plex theme for {Name}", show.Name);
            return false;
        }

        _logger.LogInformation("Saved Plex theme for {Name}", show.Name);
        _providerManager.QueueRefresh(show.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.Normal);
        return true;
    }
}
