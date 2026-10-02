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
    /// Gets the YouTube theme link for a movie or show.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="isMovie">True for a movie, false for a TV show.</param>
    /// <param name="tmdbId">TMDB id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The YouTube link, or null if ThemerrDB has none.</returns>
    public static async Task<string?> GetYouTubeUrlAsync(HttpClient http, bool isMovie, string tmdbId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(tmdbId, out var id))
        {
            return null;
        }

        var kind = isMovie ? "movies" : "tv_shows";
        using var response = await http.GetAsync($"https://app.lizardbyte.dev/ThemerrDB/{kind}/themoviedb/{id}.json", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var entry = await response.Content.ReadFromJsonAsync<Entry>(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(entry?.YouTubeThemeUrl) ? null : entry.YouTubeThemeUrl;
    }

    private sealed record Entry([property: JsonPropertyName("youtube_theme_url")] string? YouTubeThemeUrl);
}
