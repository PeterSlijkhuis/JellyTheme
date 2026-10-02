using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
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
    /// Lists movies and shows that can hold a theme but have none.
    /// </summary>
    /// <returns>The items, sorted by name.</returns>
    [HttpGet("Missing")]
    public ActionResult Missing()
        => Ok(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie],
                IsVirtualItem = false,
                Recursive = true,
            })
            .Where(ThemeService.MissingTheme)
            .OrderBy(i => i.SortName)
            .Select(i => new { i.Id, i.Name, Type = i is Movie ? "Movie" : "Series", Year = i.ProductionYear }));

    /// <summary>
    /// Searches YouTube for theme candidates.
    /// </summary>
    /// <param name="itemId">Movie or series id.</param>
    /// <param name="query">Search text; defaults to the title plus "main theme" or "opening theme".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Up to 5 videos.</returns>
    [HttpGet("Candidates/{itemId}")]
    public async Task<ActionResult> Candidates(Guid itemId, [FromQuery] string? query, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        query = string.IsNullOrWhiteSpace(query) ? $"{item.Name} {item.ProductionYear} {(item is Movie ? "main theme" : "opening theme")}" : query;
        try
        {
            var videos = await YouTubeAudio.SearchAsync(_httpClientFactory.CreateClient(), query, 5, cancellationToken).ConfigureAwait(false);
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
        if (VideoId.TryParse(videoId) is null)
        {
            return BadRequest();
        }

        try
        {
            var stream = await YouTubeAudio.OpenAsync(_httpClientFactory.CreateClient(), videoId, cancellationToken).ConfigureAwait(false);
            return stream is null ? NotFound() : File(stream, "audio/mp4");
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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 when saved.</returns>
    [HttpPost("Save/{itemId}")]
    public async Task<ActionResult> Save(Guid itemId, [FromQuery] string videoId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null || VideoId.TryParse(videoId) is null)
        {
            return BadRequest();
        }

        try
        {
            return await _themes.SaveYouTubeAsync(item, videoId, cancellationToken).ConfigureAwait(false) ? NoContent() : Conflict();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Saving YouTube theme {VideoId} for {Name} failed", videoId, item.Name);
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
