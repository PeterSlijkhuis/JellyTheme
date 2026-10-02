using System;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.JellyTheme.Tests;

public class ThemeSearchTests
{
    [Theory]
    [InlineData("The Matrix", 1999, true, "The Matrix 1999 main theme")]
    [InlineData("The Matrix (1999)", 1999, true, "The Matrix 1999 main theme")]
    [InlineData("Home Video", null, true, "Home Video main theme")]
    [InlineData("Friends", 1994, false, "Friends theme song")]
    public void BuildsDefaultQuery(string name, int? year, bool isMovie, string expected)
        => Assert.Equal(expected, ThemeSearch.DefaultQuery(name, year, isMovie));

    [Theory]
    [InlineData("The Matrix Collection", "The Matrix")]
    [InlineData("Star Wars collection", "Star Wars")]
    [InlineData("The Collection", "The")]
    [InlineData("Pixar", "Pixar")]
    public void StripsCollectionSuffix(string name, string expected)
        => Assert.Equal(expected, ThemeSearch.CollectionName(name));

    [Fact]
    public void RanksRealThemesAboveJunk()
    {
        var results = new (string Title, string Channel, int? Seconds)[]
        {
            ("Friends REACTION first time watching!", "Some Reactor", 900),
            ("Friends theme 10 HOURS", "Loops", 36000),
            ("I'll Be There for You (Theme from Friends) piano cover", "Piano Guy", 190),
            ("I'll Be There for You (Theme from \"Friends\")", "The Rembrandts - Topic", 185),
            ("Friends - Opening Credits", "TV Intros", 50),
            ("Friends #shorts", "Clips", 15),
        };

        var best = ThemeSearch.Best(results, r => ThemeSearch.Score("Friends", r.Title, r.Channel, r.Seconds is { } s ? TimeSpan.FromSeconds(s) : null), 3);

        Assert.Equal(new[] { "The Rembrandts - Topic", "TV Intros", "Piano Guy" }, best.Select(r => r.Channel));
    }

    [Fact]
    public void WordsFromTheTitleAreNotJunk()
        => Assert.True(
            ThemeSearch.Score("Rush Hour", "Rush Hour main theme", "x", TimeSpan.FromMinutes(3))
            > ThemeSearch.Score("Rush Hour", "Rush Hour main theme piano cover", "x", TimeSpan.FromMinutes(3)));

    [Fact]
    public void LiveStreamsAndShortsRankLow()
    {
        Assert.True(ThemeSearch.Score("Lost", "Lost theme", "x", null) < 0);
        Assert.True(ThemeSearch.Score("Lost", "Lost theme", "x", TimeSpan.FromSeconds(10)) < ThemeSearch.Score("Lost", "Lost theme", "x", TimeSpan.FromSeconds(60)));
    }
}
