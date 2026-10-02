using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyTheme.Tests;

public sealed class ThemeServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ShowsNeedTvdbOrTmdbIdAndNoTheme()
    {
        var show = Show();
        Assert.True(ThemeService.NeedsTheme(show));
        Assert.False(ThemeService.NeedsTheme(new Series { Path = _dir }));

        var gone = new Series { Path = Path.Combine(_dir, "deleted") };
        gone.SetProviderId(MetadataProvider.Tvdb, "81189");
        Assert.False(ThemeService.NeedsTheme(gone));

        var tmdbOnly = new Series { Path = _dir };
        tmdbOnly.SetProviderId(MetadataProvider.Tmdb, "1396");
        Assert.True(ThemeService.NeedsTheme(tmdbOnly));

        File.WriteAllBytes(Path.Combine(_dir, "theme.m4a"), [1]);
        Assert.False(ThemeService.NeedsTheme(show));
    }

    [Fact]
    public void MoviesNeedTmdbIdAndTheirOwnFolder()
    {
        var movie = new Movie { Path = Path.Combine(_dir, "The Matrix.mkv") };
        movie.SetProviderId(MetadataProvider.Tmdb, "603");
        Assert.Equal(_dir, ThemeService.ThemeFolder(movie));
        Assert.True(ThemeService.NeedsTheme(movie));

        movie.IsInMixedFolder = true;
        Assert.False(ThemeService.NeedsTheme(movie));

        var tvdbOnly = new Movie { Path = Path.Combine(_dir, "x.mkv") };
        tvdbOnly.SetProviderId(MetadataProvider.Tvdb, "1");
        Assert.False(ThemeService.NeedsTheme(tvdbOnly));
    }

    [Fact]
    public void ItemsWithoutIdsStillShowAsMissing()
    {
        var noIds = new Movie { Path = Path.Combine(_dir, "Home Video.mkv") };
        Assert.True(ThemeService.MissingTheme(noIds));
        Assert.False(ThemeService.NeedsTheme(noIds));
    }

    [Fact]
    public void CacheCleanupRemovesOnlyOldPreviews()
    {
        var old = Path.Combine(_dir, "old.m4a");
        var fresh = Path.Combine(_dir, "fresh.m4a");
        File.WriteAllText(old, "x");
        File.WriteAllText(fresh, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - YouTubeAudio.CacheLifetime - TimeSpan.FromMinutes(1));

        YouTubeAudio.CleanCache(_dir);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void CollectionsLookUpByTheirTmdbCollectionId()
    {
        var collection = new BoxSet { Path = _dir };
        Assert.Equal(_dir, ThemeService.ThemeFolder(collection));
        Assert.True(ThemeService.MissingTheme(collection));
        Assert.False(ThemeService.NeedsTheme(collection));

        collection.SetProviderId(MetadataProvider.Tmdb, "2344");
        Assert.True(ThemeService.NeedsTheme(collection));
        Assert.Equal(ThemerrDb.Kind.Collection, ThemeService.ThemerrKind(collection));
    }

    [Fact]
    public void PluginInfoLoads()
    {
        // Regression: the non-generic BasePlugin left Version and AssemblyFilePath null, so this threw and broke GET /Plugins.
        var info = NewPlugin().GetPluginInfo();
        Assert.Equal("JellyTheme", info.Name);
        Assert.NotNull(info.Version);
    }

    [Fact]
    public void PickerPageIsEmbedded()
    {
        var page = Assert.Single(NewPlugin().GetPages());
        using var html = typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath);
        Assert.NotNull(html);
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

    [Fact]
    public async Task PlexOutageStillTriesThemerrDb()
    {
        var hosts = new System.Collections.Generic.List<string>();
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new Handler(req =>
        {
            hosts.Add(req.RequestUri!.Host);
            return req.RequestUri.Host == "tvthemes.plexapp.com" ? HttpStatusCode.InternalServerError : HttpStatusCode.NotFound;
        })));
        var service = new ThemeService(http.Object, Mock.Of<IProviderManager>(), Mock.Of<IFileSystem>(), Mock.Of<IApplicationPaths>(), NullLogger<ThemeService>.Instance);
        var show = Show();
        show.SetProviderId(MetadataProvider.Tmdb, "1396");

        Assert.False(await service.TrySaveAsync(show, CancellationToken.None));
        Assert.Equal(new[] { "tvthemes.plexapp.com", "app.lizardbyte.dev" }, hosts);
    }

    private Plugin NewPlugin()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.PluginsPath).Returns(_dir);
        paths.Setup(p => p.PluginConfigurationsPath).Returns(_dir);
        return new Plugin(paths.Object, Mock.Of<IXmlSerializer>());
    }

    private Series Show()
    {
        var show = new Series { Id = Guid.NewGuid(), Path = _dir };
        show.SetProviderId(MetadataProvider.Tvdb, "81189");
        return show;
    }

    private static (ThemeService Service, Mock<IProviderManager> Providers) Service(HttpStatusCode status)
    {
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new Handler(status)));
        var providers = new Mock<IProviderManager>();
        return (new ThemeService(http.Object, providers.Object, Mock.Of<IFileSystem>(), Mock.Of<IApplicationPaths>(), NullLogger<ThemeService>.Instance), providers);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpStatusCode> status) : HttpMessageHandler
    {
        public Handler(HttpStatusCode status)
            : this(_ => status)
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status(request)) { Content = new ByteArrayContent([1]) });
    }
}
