using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Theme song files in a movie or show folder.
/// </summary>
public static class ThemeFiles
{
    /// <summary>
    /// Whether the folder already has a theme (any theme.* file or a theme-music folder).
    /// </summary>
    /// <param name="folder">The movie or show folder.</param>
    /// <returns>True if a theme exists.</returns>
    public static bool HasTheme(string folder)
        => Directory.EnumerateFiles(folder, "theme.*").Any()
           || Directory.Exists(Path.Combine(folder, "theme-music"));

    /// <summary>
    /// Writes <paramref name="source"/> to <paramref name="folder"/>/<paramref name="fileName"/>.
    /// Goes through a hidden temp file so a failed download never leaves a broken theme behind.
    /// </summary>
    /// <param name="source">Audio stream.</param>
    /// <param name="folder">The movie or show folder.</param>
    /// <param name="fileName">Theme file name, e.g. theme.mp3.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task SaveAsync(Stream source, string folder, string fileName, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(folder, ".jellytheme.part");
        try
        {
            await using (var file = File.Create(temp))
            {
                await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, Path.Combine(folder, fileName));
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
