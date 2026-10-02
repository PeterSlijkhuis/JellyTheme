using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Saves the audio track of a YouTube video as a theme.
/// </summary>
public static class YouTubeAudio
{
    /// <summary>
    /// Downloads the best AAC audio of <paramref name="url"/> into <paramref name="folder"/> as theme.m4a.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="url">YouTube video link.</param>
    /// <param name="folder">The movie or show folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if saved, false if the video has no AAC audio.</returns>
    public static async Task<bool> TrySaveAsync(HttpClient http, string url, string folder, CancellationToken cancellationToken)
    {
        // No re-encoding: YouTube's AAC track goes straight into theme.m4a, which Jellyfin plays as a theme song.
        var youtube = new YoutubeClient(http);
        var manifest = await youtube.Videos.Streams.GetManifestAsync(url, cancellationToken).ConfigureAwait(false);
        var audio = manifest.GetAudioOnlyStreams().Where(s => s.Container == Container.Mp4).TryGetWithHighestBitrate();
        if (audio is null)
        {
            return false;
        }

        await using var stream = await youtube.Videos.Streams.GetAsync(audio, cancellationToken).ConfigureAwait(false);
        await ThemeFiles.SaveAsync(stream, folder, "theme.m4a", cancellationToken).ConfigureAwait(false);
        return true;
    }
}
