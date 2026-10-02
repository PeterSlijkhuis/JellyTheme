using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Saves the audio track of a YouTube video as a theme.
/// </summary>
public static class YouTubeAudio
{
    private const double MaxMegaBytes = 30;

    /// <summary>
    /// Opens the best AAC audio track of <paramref name="url"/>.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="url">YouTube video link or id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audio stream (mp4/AAC), or null if the video has none or it is too long.</returns>
    public static async Task<Stream?> OpenAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        var youtube = new YoutubeClient(http);
        var manifest = await youtube.Videos.Streams.GetManifestAsync(url, cancellationToken).ConfigureAwait(false);
        var audio = manifest.GetAudioOnlyStreams().Where(s => s.Container == Container.Mp4).TryGetWithHighestBitrate();

        // Skips hour-long "10 hours of..." uploads: about 30 minutes of audio is plenty for a theme.
        return audio is null || audio.Size.MegaBytes > MaxMegaBytes ? null : await youtube.Videos.Streams.GetAsync(audio, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the best AAC audio of <paramref name="url"/> into <paramref name="folder"/> as theme.m4a.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="url">YouTube video link or id.</param>
    /// <param name="folder">The movie or show folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if saved, false if the video has no AAC audio.</returns>
    public static async Task<bool> TrySaveAsync(HttpClient http, string url, string folder, CancellationToken cancellationToken)
    {
        // No re-encoding: YouTube's AAC track goes straight into theme.m4a, which Jellyfin plays as a theme song.
        await using var stream = await OpenAsync(http, url, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return false;
        }

        await ThemeFiles.SaveAsync(stream, folder, "theme.m4a", cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Downloads a video's audio into <paramref name="cacheDir"/> once, so previews can seek and saving is instant.
    /// Cached files older than a day are removed.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="videoId">A validated YouTube video id (11 characters, safe as a file name).</param>
    /// <param name="cacheDir">Cache folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path of the cached audio, or null if the video has no AAC audio.</returns>
    public static async Task<string?> CacheAsync(HttpClient http, VideoId videoId, string cacheDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDir);
        var path = Path.Combine(cacheDir, videoId.Value + ".m4a");
        if (File.Exists(path))
        {
            return path;
        }

        foreach (var old in new DirectoryInfo(cacheDir).EnumerateFiles().Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // In use by another preview; next time.
            }
        }

        await using var stream = await OpenAsync(http, videoId, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        try
        {
            await ThemeFiles.SaveAsync(stream, cacheDir, videoId.Value + ".m4a", cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // A parallel request cached it first.
        }

        return path;
    }

    /// <summary>
    /// Searches YouTube videos.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="query">Search text.</param>
    /// <param name="count">Max results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The top results.</returns>
    public static async Task<IReadOnlyList<VideoSearchResult>> SearchAsync(HttpClient http, string query, int count, CancellationToken cancellationToken)
        => await new YoutubeClient(http).Search.GetVideosAsync(query, cancellationToken).CollectAsync(count).ConfigureAwait(false);
}
