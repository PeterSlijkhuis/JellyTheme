using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Fetches TV show theme songs from Plex's public theme server, keyed by TVDB id.
/// </summary>
public static class PlexThemeDownloader
{
    /// <summary>
    /// Downloads the Plex theme for <paramref name="tvdbId"/> into <paramref name="seriesPath"/> as theme.mp3.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="tvdbId">TVDB series id.</param>
    /// <param name="seriesPath">The series folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a theme was saved, false if Plex has none.</returns>
    public static async Task<bool> TryDownloadAsync(HttpClient http, string tvdbId, string seriesPath, CancellationToken cancellationToken)
    {
        if (!int.TryParse(tvdbId, out var id))
        {
            return false;
        }

        using var response = await http.GetAsync($"https://tvthemes.plexapp.com/{id}.mp3", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await ThemeFiles.SaveAsync(body, seriesPath, "theme.mp3", cancellationToken).ConfigureAwait(false);
        return true;
    }
}
