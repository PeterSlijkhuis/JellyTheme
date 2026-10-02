using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.JellyTheme.Tests;

public sealed class PlexThemeDownloaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task SavesThemeFromPlex()
    {
        Uri? requested = null;
        var http = Client(req => { requested = req.RequestUri; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }; });

        Assert.True(await PlexThemeDownloader.TryDownloadAsync(http, "81189", _dir, CancellationToken.None));
        Assert.Equal("https://tvthemes.plexapp.com/81189.mp3", requested?.ToString());
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(_dir, "theme.mp3")));
        Assert.True(PlexThemeDownloader.HasTheme(_dir));
    }

    [Fact]
    public async Task MissingThemeSavesNothing()
    {
        var http = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.False(await PlexThemeDownloader.TryDownloadAsync(http, "81189", _dir, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task NonNumericIdSkipsRequest()
    {
        var http = Client(_ => throw new InvalidOperationException("should not be called"));

        Assert.False(await PlexThemeDownloader.TryDownloadAsync(http, "../etc", _dir, CancellationToken.None));
    }

    [Fact]
    public async Task ServerErrorThrows()
    {
        var http = Client(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => PlexThemeDownloader.TryDownloadAsync(http, "81189", _dir, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task BrokenDownloadLeavesNoPartialFile()
    {
        var http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });

        await Assert.ThrowsAsync<HttpRequestException>(() => PlexThemeDownloader.TryDownloadAsync(http, "81189", _dir, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void ThemeMusicFolderCountsAsTheme()
    {
        Assert.False(PlexThemeDownloader.HasTheme(_dir));
        Directory.CreateDirectory(Path.Combine(_dir, "theme-music"));
        Assert.True(PlexThemeDownloader.HasTheme(_dir));
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));

    private sealed class BrokenStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection dropped");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("connection dropped");
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
