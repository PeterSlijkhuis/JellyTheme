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

    [Fact]
    public void SearchesSeveralWordings()
    {
        Assert.Equal(new[] { "Dune 2021 main theme", "Dune 2021 soundtrack theme", "Dune movie theme song" }, ThemeSearch.Queries("Dune", 2021, true, "Dune"));
        Assert.Equal(new[] { "Dark theme song", "Dark intro", "Dark opening theme" }, ThemeSearch.Queries("Dark", 2017, false, null));
        Assert.Equal("Le Fabuleux Destin d'Amélie Poulain 2001 theme", ThemeSearch.Queries("Amelie", 2001, true, "Le Fabuleux Destin d'Amélie Poulain")[3]);
    }

    [Fact]
    public void JoinsSearchesAndCountsAgreement()
    {
        var merged = ThemeSearch.Merge(new[] { new[] { "a", "b" }, new[] { "c", "a" } }, x => x);
        Assert.Equal(new[] { ("a", 2), ("c", 1), ("b", 1) }, merged);
    }

    [Fact]
    public void MatchesNamesWrittenDifferently()
        => Assert.True(
            ThemeSearch.Score("Spider-Man: No Way Home", "No Way Home Spider Man Main Theme", "x", TimeSpan.FromMinutes(2))
            > ThemeSearch.Score("Spider-Man: No Way Home", "Main Theme", "x", TimeSpan.FromMinutes(2)));

    [Fact]
    public void PrefersTheRightYear()
        => Assert.True(
            ThemeSearch.Score("Dune", "Dune 2021 Main Theme", "x", TimeSpan.FromMinutes(2), 2021)
            > ThemeSearch.Score("Dune", "Dune 1984 Main Theme", "x", TimeSpan.FromMinutes(2), 2021));

    [Fact]
    public void IgnoresCountryInShowNames()
        => Assert.Equal(
            ThemeSearch.Score("The Office", "The Office theme song", "x", TimeSpan.FromSeconds(40)),
            ThemeSearch.Score("The Office (US)", "The Office theme song", "x", TimeSpan.FromSeconds(40)));
}
