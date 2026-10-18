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
        "fanmade", "ai ", "minecraft", "roblox", "meme", "shorts", "#shorts", "behind the scenes", "interview", "scene", "clip",
        "lofi", "lo fi", "8 bit", "8bit", "music box", "sped up", "bass boosted", "mashup", "unofficial", "concept", "ringtone",
        "recreation", "recreated", "epic version", "full album", "full soundtrack", "complete soundtrack", "playlist", "ost cover",
    ];

    // Channels of labels and studios, besides YouTube's own "Artist - Topic".
    private static readonly string[] LabelWords = ["vevo", "records", "music", "soundtrack", "official", "studios", "pictures"];

    // Too common to tell titles apart.
    private static readonly string[] StopWords = ["the", "a", "an", "of", "and", "&", "to", "in", "on"];

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
    public static string DefaultQuery(string name, int? year, bool isMovie) => Queries(name, year, isMovie, null)[0];

    /// <summary>
    /// All searches run for an item: the default first, then wordings that find what it misses.
    /// Movie themes go by "main theme", "soundtrack" or "theme song"; show themes by "theme song", "intro" or "opening".
    /// </summary>
    /// <param name="name">Item name.</param>
    /// <param name="year">Production year, if known.</param>
    /// <param name="isMovie">True for movies, false for shows.</param>
    /// <param name="originalTitle">The title in its own language, searched too when it differs.</param>
    /// <returns>The search texts.</returns>
    public static IReadOnlyList<string> Queries(string name, int? year, bool isMovie, string? originalTitle)
    {
        var clean = CleanName(name);

        // Year only for movies: it separates remakes, while show uploads rarely mention it.
        var queries = isMovie
            ? new List<string> { $"{clean} {year} main theme", $"{clean} {year} soundtrack theme", $"{clean} movie theme song" }
            : new List<string> { $"{clean} theme song", $"{clean} intro", $"{clean} opening theme" };
        if (!string.IsNullOrWhiteSpace(originalTitle) && !Normalize(originalTitle).Equals(Normalize(clean), StringComparison.Ordinal))
        {
            queries.Add(isMovie ? $"{CleanName(originalTitle)} {year} theme" : $"{CleanName(originalTitle)} opening");
        }

        return queries.Select(q => q.Replace("  ", " ", StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Scores one search result. Higher is more likely the real theme.
    /// </summary>
    /// <param name="name">Item name.</param>
    /// <param name="title">Video title.</param>
    /// <param name="channel">Channel name.</param>
    /// <param name="duration">Video length, null for live streams.</param>
    /// <param name="year">Movie year, to tell remakes apart; null for shows.</param>
    /// <returns>The score.</returns>
    public static int Score(string name, string title, string channel, TimeSpan? duration, int? year = null)
    {
        var t = Normalize(title);

        // "The Office (US)" is "The Office" in upload titles.
        var n = Normalize(Parenthetical().Replace(CleanName(name), string.Empty));
        var score = 0;

        // Whole name, or most of its words: uploads write "Spider-Man: No Way Home" a dozen ways.
        var words = n.Split(' ').Where(w => !StopWords.Contains(w)).DefaultIfEmpty(n).ToList();
        var found = words.Count(w => Has(t, w));
        if (t.Contains(n, StringComparison.Ordinal) || found == words.Count)
        {
            score += 3;
        }
        else if (found * 2 >= words.Count)
        {
            score += 1;
        }

        // A different year in the title usually means the remake or the original.
        if (year is { } y)
        {
            var years = Year().Matches(t).Select(m => int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            score += years.Any(x => Math.Abs(x - y) <= 1) ? 1 : years.Count > 0 ? -2 : 0;
        }

        if (Has(t, "official"))
        {
            score += 1;
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
        else if (LabelWords.Any(w => Has(Normalize(channel), w)))
        {
            score += 1;
        }

        // Whole words only, and never words from the title itself ("Rush Hour", "The Piano").
        if (JunkWords.Select(w => w.Trim()).Any(w => Has(t, w) && !Has(n, w)))
        {
            score -= 5;
        }

        // Follows ThemerrDB's theme guidelines: 0:20 to 2:00 is best, over 5:00 is graded F.
        score += duration switch
        {
            null => -10, // live streams can't be downloaded
            { TotalSeconds: < 20 } => -3,
            { TotalSeconds: <= 120 } => 2,
            { TotalSeconds: <= 300 } => 1,
            { TotalMinutes: > 12 } => -4,
            _ => -1,
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

    /// <summary>
    /// Joins the results of several searches: each video once, at its best position, with how many searches found it.
    /// A video several wordings agree on is more likely the real theme.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="lists">Results per search, best first.</param>
    /// <param name="key">Identifies a video.</param>
    /// <returns>The joined results, interleaved by position.</returns>
    public static IReadOnlyList<(T Result, int Hits)> Merge<T>(IEnumerable<IReadOnlyList<T>> lists, Func<T, string> key)
        => lists.SelectMany(l => l.Select((r, i) => (r, i)))
            .OrderBy(x => x.i)
            .GroupBy(x => key(x.r))
            .Select(g => (g.First().r, g.Count()))
            .ToList();

    private static bool Has(string text, string word)
        => (" " + text + " ").Contains(" " + word + " ", StringComparison.Ordinal);

    private static string Normalize(string text)
        => NonWord().Replace(text.ToLowerInvariant(), " ").Trim();

    [GeneratedRegex(@"\s+collection\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionSuffix();

    [GeneratedRegex(@"\s*\(\d{4}\)\s*$")]
    private static partial Regex YearSuffix();

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"\b(19|20)\d\d\b")]
    private static partial Regex Year();

    [GeneratedRegex(@"[^\p{L}\p{N}#]+")]
    private static partial Regex NonWord();
}
