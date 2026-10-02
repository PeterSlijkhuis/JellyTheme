using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.JellyTheme.Tests;

public class ThemerrDbTests
{
    [Theory]
    [InlineData(ThemerrDb.Kind.Movie, "https://app.lizardbyte.dev/ThemerrDB/movies/themoviedb/603.json")]
    [InlineData(ThemerrDb.Kind.Show, "https://app.lizardbyte.dev/ThemerrDB/tv_shows/themoviedb/603.json")]
    [InlineData(ThemerrDb.Kind.Collection, "https://app.lizardbyte.dev/ThemerrDB/movie_collections/themoviedb/603.json")]
    public async Task ReturnsYouTubeLink(ThemerrDb.Kind kind, string expectedUrl)
    {
        Uri? requested = null;
        var http = Client(req =>
        {
            requested = req.RequestUri;
            return Json("""{"id": 603, "youtube_theme_url": "https://www.youtube.com/watch?v=SLBACEP6LsI"}""");
        });

        Assert.Equal("https://www.youtube.com/watch?v=SLBACEP6LsI", await ThemerrDb.GetYouTubeUrlAsync(http, kind, "603", CancellationToken.None));
        Assert.Equal(expectedUrl, requested?.ToString());
    }

    [Fact]
    public async Task EntryWithoutThemeReturnsNull()
        => Assert.Null(await ThemerrDb.GetYouTubeUrlAsync(Client(_ => Json("""{"id": 603}""")), ThemerrDb.Kind.Movie, "603", CancellationToken.None));

    [Fact]
    public async Task UnknownIdReturnsNull()
        => Assert.Null(await ThemerrDb.GetYouTubeUrlAsync(Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), ThemerrDb.Kind.Movie, "603", CancellationToken.None));

    [Fact]
    public async Task NonNumericIdSkipsRequest()
        => Assert.Null(await ThemerrDb.GetYouTubeUrlAsync(Client(_ => throw new InvalidOperationException("should not be called")), ThemerrDb.Kind.Movie, "../x", CancellationToken.None));

    [Fact]
    public void BuildsPrefilledSubmitLink()
    {
        var url = new Uri(ThemerrDb.SubmitUrl(ThemerrDb.Kind.Movie, "The Matrix", 1999, "603", "SLBACEP6LsI"));
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("github.com", url.Host);
        Assert.Equal("/LizardByte/ThemerrDB/issues/new", url.AbsolutePath);
        Assert.Equal("theme.yml", query["template"]);
        Assert.Equal("request-theme", query["labels"]);
        Assert.Equal("[THEME]: The Matrix (1999)", query["title"]);
        Assert.Equal("https://www.themoviedb.org/movie/603", query["database_url"]);
        Assert.Equal("https://www.youtube.com/watch?v=SLBACEP6LsI", query["youtube_theme_url"]);
        Assert.Null(query["replacement_reason"]);

        var show = System.Web.HttpUtility.ParseQueryString(new Uri(ThemerrDb.SubmitUrl(ThemerrDb.Kind.Show, "Friends & Co", null, "1668", "x")).Query);
        Assert.Equal("[THEME]: Friends & Co", show["title"]);
        Assert.Equal("https://www.themoviedb.org/tv/1668", show["database_url"]);

        var collection = System.Web.HttpUtility.ParseQueryString(new Uri(ThemerrDb.SubmitUrl(ThemerrDb.Kind.Collection, "The Matrix Collection", null, "2344", "x", "https://www.youtube.com/watch?v=old")).Query);
        Assert.Equal("https://www.themoviedb.org/collection/2344", collection["database_url"]);
        Assert.Contains("https://www.youtube.com/watch?v=old", collection["replacement_reason"]);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
