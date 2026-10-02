using System.IO;
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
    /// <summary>File name Jellyfin picks up as a theme song.</summary>
    public const string ThemeFileName = "theme.mp3";

    /// <summary>
    /// Whether the show folder already has a theme (theme.mp3 or a theme-music folder).
    /// </summary>
    /// <param name="seriesPath">The series folder.</param>
    /// <returns>True if a theme exists.</returns>
    public static bool HasTheme(string seriesPath)
        => File.Exists(Path.Combine(seriesPath, ThemeFileName))
           || Directory.Exists(Path.Combine(seriesPath, "theme-music"));

    /// <summary>
    /// Downloads the Plex theme for <paramref name="tvdbId"/> into <paramref name="seriesPath"/>.
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

        // Write to a temp file first so a failed download never leaves a truncated theme.mp3.
        var target = Path.Combine(seriesPath, ThemeFileName);
        var temp = target + ".part";
        try
        {
            await using (var file = File.Create(temp))
            {
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, target);
        }
        finally
        {
            File.Delete(temp);
        }

        return true;
    }
}
