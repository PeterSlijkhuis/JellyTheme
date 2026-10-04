using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Fetches a theme as soon as a movie or show gets its metadata, so new items don't wait for the daily task.
/// </summary>
public sealed class NewItemListener : BackgroundService
{
    private static readonly TimeSpan ScanCheckInterval = TimeSpan.FromSeconds(10);

    private readonly ILibraryManager _libraryManager;
    private readonly ThemeService _themes;
    private readonly ILogger<NewItemListener> _logger;
    private readonly Channel<BaseItem> _queue = Channel.CreateUnbounded<BaseItem>();

    // Items already tried since startup; ItemUpdated fires often and source misses shouldn't be re-requested each time.
    private readonly ConcurrentDictionary<Guid, byte> _tried = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NewItemListener"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="themes">Theme service.</param>
    /// <param name="logger">Logger.</param>
    public NewItemListener(ILibraryManager libraryManager, ThemeService themes, ILogger<NewItemListener> logger)
    {
        _libraryManager = libraryManager;
        _themes = themes;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemUpdated;
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _queue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One at a time, and never during a library scan: downloads and the refresh each saved theme queues
        // would otherwise compete with the scan for disk, network and the database.
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                while (_libraryManager.IsScanRunning)
                {
                    await Task.Delay(ScanCheckInterval, stoppingToken).ConfigureAwait(false);
                }

                try
                {
                    await _themes.TrySaveAsync(item, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Could not add a theme for {Name}", item.Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        // Provider ids arrive with the first metadata refresh, which raises ItemUpdated, not ItemAdded.
        // ItemUpdated fires for every item on every scan, on the scan's own thread, so only checks that don't
        // touch the disk run here: each movie, show or collection is queued once per server start.
        var item = e.Item;
        if (item is Movie or Series or BoxSet
            && !_tried.ContainsKey(item.Id)
            && ThemeService.HasLookupId(item)
            && _tried.TryAdd(item.Id, 0))
        {
            _queue.Writer.TryWrite(item);
        }
    }
}
