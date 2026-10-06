using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace AstroPostMaster.Handoff.Tests;

public class HandoffServerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "apm-srv-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _images = [];

    public HandoffServerTests()
    {
        Directory.CreateDirectory(_dir);
        for (var i = 0; i < 2; i++)
        {
            var path = Path.Combine(_dir, $"{i + 1:00}.jpg");
            File.WriteAllBytes(path, [0xFF, 0xD8, (byte)i, 0xFF, 0xD9]);
            _images.Add(path);
        }
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private HandoffPackage Package => new("Heart Nebula", "Heart Nebula (IC 1805)\n\n#heartnebula", _images);

    private static HttpClient Client() => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = false,
    });

    private static Task<HandoffServer> Start(HandoffPackage package, bool https = true, int port = 0, TimeProvider? time = null) =>
        HandoffServer.StartAsync(package, IPAddress.Loopback,
            https ? HandoffCertificate.Create([IPAddress.Loopback], DateTimeOffset.UtcNow) : null,
            preferredPort: port, time: time);

    [Fact]
    public async Task ServesPageManifestAndImagesOverHttps()
    {
        await using var server = await Start(Package);
        using var client = Client();
        var received = 0;
        server.RequestReceived += () => received++;

        Assert.Equal("https", server.Url.Scheme);
        var page = await client.GetAsync(server.Url);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Copy caption", await page.Content.ReadAsStringAsync());

        using var manifest = JsonDocument.Parse(await client.GetStringAsync(new Uri(server.Url, "manifest.json")));
        Assert.Equal("Heart Nebula", manifest.RootElement.GetProperty("title").GetString());
        Assert.Equal("Heart Nebula (IC 1805)\n\n#heartnebula", manifest.RootElement.GetProperty("caption").GetString());
        Assert.Equal(new[] { "01.jpg", "02.jpg" }, manifest.RootElement.GetProperty("images").EnumerateArray().Select(e => e.GetString()));

        var image = await client.GetAsync(new Uri(server.Url, "img/1"));
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(File.ReadAllBytes(_images[1]), await image.Content.ReadAsByteArrayAsync());

        Assert.True(server.HasReceivedRequest);
        Assert.Equal(3, received);
    }

    [Fact]
    public async Task UnknownTokenAndOutOfRangeImagesAreNotFound()
    {
        await using var server = await Start(Package);
        using var client = Client();
        var root = new Uri(server.Url.GetLeftPart(UriPartial.Authority));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(root, "/wrongtoken/"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(root, "/"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(server.Url, "img/5"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(server.Url, "img/-1"))).StatusCode);
        Assert.False(server.HasReceivedRequest);
    }

    [Fact]
    public async Task MissingTrailingSlashRedirects()
    {
        await using var server = await Start(Package);
        using var client = Client();
        var noSlash = new Uri(server.Url.ToString().TrimEnd('/'));

        var response = await client.GetAsync(noSlash);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith(server.Url.AbsolutePath, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task WorksOverPlainHttp()
    {
        await using var server = await Start(Package, https: false);
        using var client = Client();
        Assert.Equal("http", server.Url.Scheme);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(server.Url)).StatusCode);
    }

    [Fact]
    public async Task FallsBackToAnotherPortWhenPreferredIsBusy()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var busyPort = ((IPEndPoint)blocker.LocalEndpoint).Port;
        try
        {
            await using var server = await Start(Package, port: busyPort);
            using var client = Client();
            Assert.NotEqual(busyPort, server.Url.Port);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(server.Url)).StatusCode);
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Fact]
    public async Task StopsAfterIdleTimeout()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var server = await Start(Package, time: time);
        var stopped = new TaskCompletionSource();
        server.Stopped += () => stopped.TrySetResult();

        time.Advance(TimeSpan.FromMinutes(16));

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var client = Client();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync(server.Url));
    }

    [Fact]
    public async Task ContentRootIsTheAppFolder_NotTheWorkingDirectory()
    {
        await using var server = await Start(Package, https: false);
        Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.TrimEndingDirectorySeparator(server.ContentRoot));
        Assert.False(server.ReloadsConfigOnChange);
    }
}
