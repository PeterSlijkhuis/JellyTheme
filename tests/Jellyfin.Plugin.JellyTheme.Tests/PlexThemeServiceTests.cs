using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyTheme.Tests;

public sealed class PlexThemeServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void OnlyShowsWithTvdbIdAndNoThemeNeedOne()
    {
        var show = Show();
        Assert.True(PlexThemeService.NeedsTheme(show));

        Assert.False(PlexThemeService.NeedsTheme(new Series { Path = _dir }));
        Assert.False(PlexThemeService.NeedsTheme(new Movie { Path = _dir, ProviderIds = show.ProviderIds }));

        File.WriteAllBytes(Path.Combine(_dir, "theme.mp3"), [1]);
        Assert.False(PlexThemeService.NeedsTheme(show));
    }

    [Fact]
    public async Task SavedThemeQueuesRefresh()
    {
        var (service, providers) = Service(HttpStatusCode.OK);
        var show = Show();

        Assert.True(await service.TrySaveAsync(show, CancellationToken.None));
        providers.Verify(p => p.QueueRefresh(show.Id, It.IsAny<MetadataRefreshOptions>(), RefreshPriority.Normal), Times.Once);
    }

    [Fact]
    public async Task PlexMissOrErrorQueuesNothing()
    {
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
        {
            var (service, providers) = Service(status);

            Assert.False(await service.TrySaveAsync(Show(), CancellationToken.None));
            providers.VerifyNoOtherCalls();
        }
    }

    private Series Show()
    {
        var show = new Series { Id = Guid.NewGuid(), Path = _dir };
        show.SetProviderId(MetadataProvider.Tvdb, "81189");
        return show;
    }

    private static (PlexThemeService Service, Mock<IProviderManager> Providers) Service(HttpStatusCode status)
    {
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new Handler(status)));
        var providers = new Mock<IProviderManager>();
        return (new PlexThemeService(http.Object, providers.Object, Mock.Of<IFileSystem>(), NullLogger<PlexThemeService>.Instance), providers);
    }

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent([1]) });
    }
}
