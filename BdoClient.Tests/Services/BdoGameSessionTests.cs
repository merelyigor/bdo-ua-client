using System.Net;
using System.Net.Http;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Services;

public sealed class BdoGameSessionTests
{
    [Fact]
    public void CreateForTests_ComposesBdoServicesInCanonicalGameScope()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            using var client = new HttpClient(new StubHandler());
            using var session = BdoGameSession.CreateForTests(
                paths, new TestLogger(), AppVersionInfo.FromRawVersion("1.2.7"), client);

            Assert.Equal("black-desert-online", session.Descriptor.Id);
            Assert.Equal("Black Desert Online", session.Descriptor.DisplayName);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "games", "black-desert-online"),
                session.PersistencePaths.Root);
            Assert.Equal(session.PersistencePaths.ConfigFile,
                Path.Combine(session.PersistencePaths.Root, "config.json"));
            Assert.NotNull(session.InstallationStateStore);
            Assert.NotNull(session.BackupStore);
            Assert.NotNull(session.GameDetector);
            Assert.NotNull(session.ApiClient);
            Assert.NotNull(session.LocalizationInstaller);
            Assert.NotNull(session.LocalizationStateService);
            Assert.NotNull(session.LocalizationCompatibilityService);
            Assert.Equal(Path.Combine(paths.CacheDir, "release-feed.json"), session.ReleaseFeedCacheStore.CacheFile);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Dispose_IsIdempotent_StopsPollerAndDoesNotDisposeInjectedClient()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            var handler = new TrackingHandler();
            using var client = new HttpClient(handler);
            var session = BdoGameSession.CreateForTests(
                paths, new TestLogger(), AppVersionInfo.FromRawVersion("1.2.7"), client);

            session.ReleaseFeedPoller.Start(null);
            Assert.True(session.ReleaseFeedPoller.IsRunning);

            session.Dispose();
            session.Dispose();

            Assert.False(session.ReleaseFeedPoller.IsRunning);
            Assert.False(handler.WasDisposed);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Dispose_WhenSessionOwnsClient_DisposesInjectedClient()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            var handler = new TrackingHandler();
            var client = new HttpClient(handler);
            var session = BdoGameSession.CreateForTests(
                paths, new TestLogger(), AppVersionInfo.FromRawVersion("1.2.7"), client, ownsHttpClient: true);

            session.Dispose();

            Assert.True(handler.WasDisposed);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-session-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool WasDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
