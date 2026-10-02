using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Scheduled task that adds Plex theme songs to TV shows that have none.
/// </summary>
public class PlexThemeTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlexThemeTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexThemeTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="httpClientFactory">Http client factory.</param>
    /// <param name="logger">Logger.</param>
    public PlexThemeTask(ILibraryManager libraryManager, IHttpClientFactory httpClientFactory, ILogger<PlexThemeTask> logger)
    {
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Download TV theme songs from Plex";

    /// <inheritdoc />
    public string Key => "JellyThemePlexTv";

    /// <inheritdoc />
    public string Description => "Saves theme.mp3 for every TV show that has a TVDB id and no theme yet.";

    /// <inheritdoc />
    public string Category => "JellyTheme";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var series = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
        }).Where(s => !string.IsNullOrEmpty(s.Path) && !PlexThemeDownloader.HasTheme(s.Path)).ToList();

        var http = _httpClientFactory.CreateClient();
        int saved = 0;
        for (int i = 0; i < series.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var show = series[i];
            if (show.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId))
            {
                try
                {
                    if (await PlexThemeDownloader.TryDownloadAsync(http, tvdbId, show.Path, cancellationToken).ConfigureAwait(false))
                    {
                        saved++;
                        _logger.LogInformation("Saved Plex theme for {Name}", show.Name);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not save Plex theme for {Name}", show.Name);
                }
            }

            progress.Report(100.0 * (i + 1) / series.Count);
        }

        // ponytail: themes are picked up on the next library scan; trigger a refresh here if that lag bothers anyone.
        _logger.LogInformation("Saved {Saved} Plex themes, checked {Count} shows without one", saved, series.Count);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromDays(1).Ticks }];
}
