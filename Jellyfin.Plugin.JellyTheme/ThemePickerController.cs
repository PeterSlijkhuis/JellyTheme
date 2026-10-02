using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using YoutubeExplode.Videos;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Backs the "Missing themes" page: list items without a theme, search YouTube, preview, save the pick. Admins only.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("JellyTheme")]
public class ThemePickerController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ThemeService _themes;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ThemePickerController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemePickerController"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="themes">Theme service.</param>
    /// <param name="httpClientFactory">Http client factory.</param>
    /// <param name="logger">Logger.</param>
    public ThemePickerController(ILibraryManager libraryManager, ThemeService themes, IHttpClientFactory httpClientFactory, ILogger<ThemePickerController> logger)
    {
        _libraryManager = libraryManager;
        _themes = themes;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Lists movies, shows and collections that can hold a theme but have none.
    /// </summary>
    /// <param name="all">Also list items that already have a theme, to replace it.</param>
    /// <returns>The items, sorted by name.</returns>
    [HttpGet("Missing")]
    public ActionResult Missing([FromQuery] bool all = false)
        => Ok(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie, BaseItemKind.BoxSet],
                IsVirtualItem = false,
                Recursive = true,
            })
            .Where(all ? ThemeService.CanHoldTheme : ThemeService.MissingTheme)
            .OrderBy(i => i.SortName)
            .Select(i => new
            {
                i.Id,
                i.Name,
                Type = i switch { Movie => "Movie", BoxSet => "Collection", _ => "Series" },
                Year = i.ProductionYear,
                Added = i.DateCreated,
                Query = DefaultQuery(i),
                HasTheme = all && !ThemeService.MissingTheme(i),
            }));

    /// <summary>
    /// Searches YouTube for theme candidates.
    /// </summary>
    /// <param name="itemId">Movie or series id.</param>
    /// <param name="query">Search text; defaults to <see cref="ThemeSearch.DefaultQuery"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 5 most theme-like of the top 20 YouTube results.</returns>
    [HttpGet("Candidates/{itemId}")]
    public async Task<ActionResult> Candidates(Guid itemId, [FromQuery] string? query, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        query = string.IsNullOrWhiteSpace(query) ? DefaultQuery(item) : query;
        try
        {
            var found = await YouTubeAudio.SearchAsync(_httpClientFactory.CreateClient(), query, 20, cancellationToken).ConfigureAwait(false);
            var videos = ThemeSearch.Best(found, v => ThemeSearch.Score(SearchName(item), v.Title, v.Author.ChannelTitle, v.Duration), 5);
            _themes.PrefetchPreviews(videos.Select(v => v.Id));
            return Ok(videos.Select(v => new { Id = v.Id.Value, v.Title, Author = v.Author.ChannelTitle, Duration = v.Duration?.TotalSeconds }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube search failed for {Query}", query);
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Streams a video's audio so the page can play it before saving.
    /// </summary>
    /// <param name="videoId">YouTube video id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audio (mp4/AAC).</returns>
    [HttpGet("Preview/{videoId}")]
    public async Task<ActionResult> Preview(string videoId, CancellationToken cancellationToken)
    {
        if (VideoId.TryParse(videoId) is not { } id)
        {
            return BadRequest();
        }

        try
        {
            // Served from a cached file with range support: Safari and iOS refuse to play audio without it.
            var path = await _themes.CachePreviewAsync(id, cancellationToken).ConfigureAwait(false);
            return path is null ? NotFound() : PhysicalFile(path, "audio/mp4", enableRangeProcessing: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube preview failed for {VideoId}", videoId);
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Saves the picked video's audio as the item's theme.
    /// </summary>
    /// <param name="itemId">Movie or series id.</param>
    /// <param name="videoId">YouTube video id.</param>
    /// <param name="replace">Replace the theme the item already has; the old files are kept as hidden backups.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pre-filled ThemerrDB submit link, or null when the item has no TMDB id.</returns>
    [HttpPost("Save/{itemId}")]
    public async Task<ActionResult> Save(Guid itemId, [FromQuery] string videoId, [FromQuery] bool replace, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null || VideoId.TryParse(videoId) is not { } id)
        {
            return BadRequest();
        }

        try
        {
            if (!await _themes.SaveYouTubeAsync(item, id, replace, cancellationToken).ConfigureAwait(false))
            {
                return Conflict();
            }

            // Items without a TMDB id can't be submitted: ThemerrDB keys everything on it.
            string? submitUrl = null;
            if (item.TryGetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tmdb, out var tmdbId))
            {
                var kind = ThemeService.ThemerrKind(item);
                submitUrl = ThemerrDb.SubmitUrl(kind, ThemeSearch.CleanName(item.Name), item is BoxSet ? null : item.ProductionYear, tmdbId, id.Value, await CurrentThemerrThemeAsync(kind, tmdbId, cancellationToken).ConfigureAwait(false));
            }

            return Ok(new { SubmitUrl = submitUrl });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Saving YouTube theme {VideoId} for {Name} failed", videoId, item.Name);
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    // ThemerrDB asks for a reason when a submission replaces its current theme. Having one here means it failed to download.
    private async Task<string?> CurrentThemerrThemeAsync(ThemerrDb.Kind kind, string tmdbId, CancellationToken cancellationToken)
    {
        try
        {
            return await ThemerrDb.GetYouTubeUrlAsync(_httpClientFactory.CreateClient(), kind, tmdbId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string SearchName(BaseItem item) => item is BoxSet ? ThemeSearch.CollectionName(item.Name) : item.Name;

    // Collections search like a movie without a year: "The Matrix main theme".
    private static string DefaultQuery(BaseItem item)
        => ThemeSearch.DefaultQuery(SearchName(item), item is BoxSet ? null : item.ProductionYear, item is not Series);
}
