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
/// Scheduled task that adds theme songs to movies and TV shows that have none.
/// </summary>
public class ThemeTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ThemeService _themes;
    private readonly ILogger<ThemeTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="themes">Theme service.</param>
    /// <param name="logger">Logger.</param>
    public ThemeTask(ILibraryManager libraryManager, ThemeService themes, ILogger<ThemeTask> logger)
    {
        _libraryManager = libraryManager;
        _themes = themes;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Download theme songs";

    /// <inheritdoc />
    public string Key => "JellyThemeDownload";

    /// <inheritdoc />
    public string Description => "Saves a theme for every movie, TV show and collection that has none yet, from Plex and ThemerrDB. New items are also handled as soon as their metadata arrives.";

    /// <inheritdoc />
    public string Category => "JellyTheme";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _themes.CleanPreviewCache();
        var series = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie, BaseItemKind.BoxSet],
            IsVirtualItem = false,
            Recursive = true,
        }).Where(ThemeService.NeedsTheme).ToList();

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

        _logger.LogInformation("Saved {Saved} themes, checked {Count} items without one", saved, series.Count);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromDays(1).Ticks }];
}
