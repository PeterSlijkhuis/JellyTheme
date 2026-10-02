using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyTheme;

/// <summary>
/// Builds the YouTube search for an item and ranks the results so real theme songs come first.
/// </summary>
public static partial class ThemeSearch
{
    // Signs of a real theme upload.
    private static readonly string[] ThemeWords = ["theme", "intro", "opening", "main title", "soundtrack", "ost", "score", "title sequence", "credits"];

    // Signs of something else: covers, reactions, edits, loops, trailers.
    private static readonly string[] JunkWords =
    [
        "reaction", "review", "trailer", "teaser", "explained", "breakdown", "analysis", "cover", "piano", "guitar", "violin",
        "drum", "tutorial", "lesson", "how to", "remix", "slowed", "reverb", "nightcore", "8d", "hour", "hours", "loop",
        "extended", "compilation", "ranking", "top 10", "every ", "parody", "karaoke", "instrumental cover", "fan made",
        "fanmade", "ai ", "minecraft", "roblox", "meme", "shorts", "#shorts", "behind the scenes", "interview",
    ];

    /// <summary>
    /// The name without a trailing "(1999)", which items without metadata carry.
    /// </summary>
    /// <param name="name">Item name.</param>
    /// <returns>The clean name.</returns>
    public static string CleanName(string name) => YearSuffix().Replace(name, string.Empty).Trim();

    /// <summary>
    /// What to search for a collection: "The Matrix Collection" becomes "The Matrix".
    /// </summary>
    /// <param name="name">Collection name.</param>
    /// <returns>The name without a trailing "Collection".</returns>
    public static string CollectionName(string name) => CollectionSuffix().Replace(name, string.Empty).Trim();

    /// <summary>
    /// The default search for a movie or show, e.g. "The Matrix 1999 main theme" or "Friends theme song".
    /// </summary>
    /// <param name="name">Item name.</param>
    /// <param name="year">Production year, if known.</param>
    /// <param name="isMovie">True for movies, false for shows.</param>
    /// <returns>The search text.</returns>
    public static string DefaultQuery(string name, int? year, bool isMovie)
    {
        var clean = CleanName(name);

        // Year only for movies: it separates remakes, while show uploads rarely mention it.
        return isMovie ? $"{clean} {year} main theme".Replace("  ", " ", StringComparison.Ordinal) : $"{clean} theme song";
    }

    /// <summary>
    /// Scores one search result. Higher is more likely the real theme.
    /// </summary>
    /// <param name="name">Item name.</param>
    /// <param name="title">Video title.</param>
    /// <param name="channel">Channel name.</param>
    /// <param name="duration">Video length, null for live streams.</param>
    /// <returns>The score.</returns>
    public static int Score(string name, string title, string channel, TimeSpan? duration)
    {
        var t = Normalize(title);
        var n = Normalize(CleanName(name));
        var score = 0;

        if (t.Contains(n, StringComparison.Ordinal))
        {
            score += 3;
        }

        if (ThemeWords.Any(w => t.Contains(w, StringComparison.Ordinal)))
        {
            score += 2;
        }

        // "Artist - Topic" channels are YouTube's official audio uploads.
        if (channel.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        // Whole words only, and never words from the title itself ("Rush Hour", "The Piano").
        if (JunkWords.Select(w => w.Trim()).Any(w => Has(t, w) && !Has(n, w)))
        {
            score -= 5;
        }

        score += duration switch
        {
            null => -10, // live streams can't be downloaded
            { TotalSeconds: < 25 } => -3,
            { TotalMinutes: <= 8 } => 1,
            { TotalMinutes: > 12 } => -4,
            _ => 0,
        };

        return score;
    }

    /// <summary>
    /// Orders results best first, keeping YouTube's order between equal scores.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="results">Search results.</param>
    /// <param name="score">Scores one result.</param>
    /// <param name="count">How many to keep.</param>
    /// <returns>The best results.</returns>
    public static IReadOnlyList<T> Best<T>(IEnumerable<T> results, Func<T, int> score, int count)
        => results.Select((r, i) => (r, i, s: score(r))).OrderByDescending(x => x.s).ThenBy(x => x.i).Take(count).Select(x => x.r).ToList();

    private static bool Has(string text, string word)
        => (" " + text + " ").Contains(" " + word + " ", StringComparison.Ordinal);

    private static string Normalize(string text)
        => NonWord().Replace(text.ToLowerInvariant(), " ").Trim();

    [GeneratedRegex(@"\s+collection\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionSuffix();

    [GeneratedRegex(@"\s*\(\d{4}\)\s*$")]
    private static partial Regex YearSuffix();

    [GeneratedRegex(@"[^\p{L}\p{N}#]+")]
    private static partial Regex NonWord();
}
