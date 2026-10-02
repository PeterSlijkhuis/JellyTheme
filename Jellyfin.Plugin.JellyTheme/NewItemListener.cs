using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Fetches a theme as soon as a movie or show gets its metadata, so new items don't wait for the daily task.
/// </summary>
public sealed class NewItemListener : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ThemeService _themes;

    // Items already tried since startup; ItemUpdated fires often and source misses shouldn't be re-requested each time.
    private readonly ConcurrentDictionary<Guid, byte> _tried = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NewItemListener"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="themes">Theme service.</param>
    public NewItemListener(ILibraryManager libraryManager, ThemeService themes)
    {
        _libraryManager = libraryManager;
        _themes = themes;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemUpdated;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemUpdated;
        return Task.CompletedTask;
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        // Provider ids arrive with the first metadata refresh, which raises ItemUpdated, not ItemAdded.
        if (ThemeService.NeedsTheme(e.Item) && _tried.TryAdd(e.Item.Id, 0))
        {
            _ = _themes.TrySaveAsync(e.Item, CancellationToken.None);
        }
    }
}
