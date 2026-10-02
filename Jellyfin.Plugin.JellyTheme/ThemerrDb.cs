using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Looks up community-picked YouTube theme links in ThemerrDB (https://github.com/LizardByte/ThemerrDB), keyed by TMDB id.
/// </summary>
public static class ThemerrDb
{
    /// <summary>
    /// The ThemerrDB sections JellyTheme uses.
    /// </summary>
    public enum Kind
    {
        /// <summary>A movie (movies/themoviedb).</summary>
        Movie,

        /// <summary>A TV show (tv_shows/themoviedb).</summary>
        Show,

        /// <summary>A movie collection (movie_collections/themoviedb), keyed by TMDB collection id.</summary>
        Collection,
    }

    /// <summary>
    /// Gets the YouTube theme link for a movie, show or collection.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="kind">What the TMDB id belongs to.</param>
    /// <param name="tmdbId">TMDB id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The YouTube link, or null if ThemerrDB has none.</returns>
    public static async Task<string?> GetYouTubeUrlAsync(HttpClient http, Kind kind, string tmdbId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(tmdbId, out var id))
        {
            return null;
        }

        var section = kind switch { Kind.Movie => "movies", Kind.Show => "tv_shows", _ => "movie_collections" };
        using var response = await http.GetAsync($"https://app.lizardbyte.dev/ThemerrDB/{section}/themoviedb/{id}.json", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var entry = await response.Content.ReadFromJsonAsync<Entry>(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(entry?.YouTubeThemeUrl) ? null : entry.YouTubeThemeUrl;
    }

    /// <summary>
    /// Link to ThemerrDB's "Add/Update Theme Song" issue form on GitHub, pre-filled the way its README asks:
    /// the TMDB page as Database URL and a plain watch?v= link as the YouTube URL. The form fields become the issue body.
    /// </summary>
    /// <param name="kind">What the TMDB id belongs to.</param>
    /// <param name="name">Item name, only for the title; ThemerrDB's bot renames the issue anyway.</param>
    /// <param name="year">Production year, if known.</param>
    /// <param name="tmdbId">TMDB id; ThemerrDB keys everything on it.</param>
    /// <param name="videoId">YouTube video id.</param>
    /// <param name="replacing">The theme ThemerrDB has now, when this replaces it; the form asks for a reason then.</param>
    /// <returns>The form link.</returns>
    public static string SubmitUrl(Kind kind, string name, int? year, string tmdbId, string videoId, string? replacing = null)
    {
        var title = $"[THEME]: {name}{(year is null ? string.Empty : $" ({year})")}";
        var database = $"https://www.themoviedb.org/{kind switch { Kind.Movie => "movie", Kind.Show => "tv", _ => "collection" }}/{tmdbId}";
        var url = "https://github.com/LizardByte/ThemerrDB/issues/new?assignees=&labels=request-theme&template=theme.yml"
                  + "&title=" + Uri.EscapeDataString(title)
                  + "&database_url=" + Uri.EscapeDataString(database)
                  + "&youtube_theme_url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=" + videoId);
        return replacing is null
            ? url
            : url + "&replacement_reason=" + Uri.EscapeDataString($"The current theme ({replacing}) could not be downloaded; it may be removed, private or region locked.");
    }

    private sealed record Entry([property: JsonPropertyName("youtube_theme_url")] string? YouTubeThemeUrl);
}
