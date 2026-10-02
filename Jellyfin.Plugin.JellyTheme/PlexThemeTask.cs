using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Scheduled task that adds Plex theme songs to TV shows that have none.
/// </summary>
public class PlexThemeTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly PlexThemeService _themes;
    private readonly ILogger<PlexThemeTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexThemeTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="themes">Plex theme service.</param>
    /// <param name="logger">Logger.</param>
    public PlexThemeTask(ILibraryManager libraryManager, PlexThemeService themes, ILogger<PlexThemeTask> logger)
    {
        _libraryManager = libraryManager;
        _themes = themes;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Download TV theme songs from Plex";

    /// <inheritdoc />
    public string Key => "JellyThemePlexTv";

    /// <inheritdoc />
    public string Description => "Saves theme.mp3 for every TV show that has a TVDB id and no theme yet. New shows are also handled as soon as their metadata arrives.";

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
        }).Where(PlexThemeService.NeedsTheme).ToList();

        int saved = 0;
        for (int i = 0; i < series.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _themes.TrySaveAsync(series[i], cancellationToken).ConfigureAwait(false))
            {
                saved++;
            }

            progress.Report(100.0 * (i + 1) / series.Count);
        }

        _logger.LogInformation("Saved {Saved} Plex themes, checked {Count} shows without one", saved, series.Count);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromDays(1).Ticks }];
}
