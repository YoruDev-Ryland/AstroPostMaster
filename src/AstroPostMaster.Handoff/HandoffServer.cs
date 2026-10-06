using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AstroPostMaster.Handoff;

public sealed record HandoffPackage(string Title, string Caption, IReadOnlyList<string> ImagePaths);

/// <summary>
/// Serves one post to a phone on the LAN: /{token}/ (page), /{token}/manifest.json, /{token}/img/{i}.
/// Every other path is 404. Shuts itself down after <c>idleTimeout</c> without valid requests.
/// </summary>
public sealed class HandoffServer : IAsyncDisposable
{
    public const int DefaultPort = 48443;
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(30);

    private readonly HandoffPackage _package;
    private readonly IReadOnlyList<string> _images;
    private readonly string _token;
    private readonly TimeProvider _time;
    private readonly TimeSpan _idleTimeout;
    private readonly string _manifestJson;
    private WebApplication? _app;
    private ITimer? _idleTimer;
    private long _lastActivityTicks;
    private int _stopped;

    public Uri Url { get; private set; } = null!;
    internal string ContentRoot => _app!.Environment.ContentRootPath;
    internal bool ReloadsConfigOnChange => _app!.Configuration.GetValue("hostBuilder:reloadConfigOnChange", true);
    public bool HasReceivedRequest { get; private set; }
    public event Action? RequestReceived;
    public event Action? Stopped;

    private HandoffServer(HandoffPackage package, TimeProvider time, TimeSpan idleTimeout)
    {
        _package = package;
        _images = package.ImagePaths.Select(Path.GetFullPath).ToList();
        _token = HandoffToken.Create();
        _time = time;
        _idleTimeout = idleTimeout;
        _manifestJson = JsonSerializer.Serialize(new
        {
            title = package.Title,
            caption = package.Caption,
            images = _images.Select((_, i) => $"{i + 1:00}.jpg").ToArray(),
        });
        _lastActivityTicks = time.GetUtcNow().UtcTicks;
    }

    /// <param name="certificate">Serve HTTPS with this certificate, or plain HTTP when null.</param>
    /// <param name="preferredPort">Tried first so phones can remember the certificate exception; 0 = any free port.</param>
    public static async Task<HandoffServer> StartAsync(HandoffPackage package, IPAddress bindAddress, X509Certificate2? certificate,
        int preferredPort = DefaultPort, TimeSpan? idleTimeout = null, TimeProvider? time = null, CancellationToken cancellationToken = default)
    {
        var server = new HandoffServer(package, time ?? TimeProvider.System, idleTimeout ?? DefaultIdleTimeout);
        await server.ListenAsync(bindAddress, certificate, preferredPort, cancellationToken);
        return server;
    }

    public ValueTask DisposeAsync() => new(StopAsync(raiseStopped: false));

    private async Task ListenAsync(IPAddress bindAddress, X509Certificate2? certificate, int preferredPort, CancellationToken cancellationToken)
    {
        int[] ports = preferredPort == 0 ? [0] : [preferredPort, 0];
        foreach (var port in ports)
        {
            var app = Build(bindAddress, port, certificate);
            try
            {
                await app.StartAsync(cancellationToken);
            }
            catch (IOException) when (port != 0)
            {
                await app.DisposeAsync();
                continue;
            }

            _app = app;
            var bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
            var host = bindAddress.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{bindAddress}]" : bindAddress.ToString();
            Url = new Uri($"{(certificate is null ? "http" : "https")}://{host}:{bound.Port}/{_token}/");
            _idleTimer = _time.CreateTimer(_ => CheckIdle(), null, IdleCheckInterval, IdleCheckInterval);
            return;
        }
    }

    private WebApplication Build(IPAddress bindAddress, int port, X509Certificate2? certificate)
    {
        // Content root = app folder and no config reload: otherwise the host watches the working directory
        // recursively (inotify), which can be the user's whole home folder when launched from a desktop entry.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = ["--hostBuilder:reloadConfigOnChange=false"],
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(bindAddress, port, listen =>
        {
            if (certificate is not null) listen.UseHttps(certificate);
        }));

        var app = builder.Build();
        var page = LoadPage();

        app.MapGet("/{token}", (string token, HttpContext context) =>
        {
            if (!Accept(token)) return Results.NotFound();
            return context.Request.Path.Value!.EndsWith('/')
                ? Results.Content(page, "text/html; charset=utf-8")
                : Results.Redirect($"/{token}/");
        });
        app.MapGet("/{token}/manifest.json", (string token) =>
            Accept(token) ? Results.Content(_manifestJson, "application/json; charset=utf-8") : Results.NotFound());
        app.MapGet("/{token}/img/{index:int}", (string token, int index) =>
            index >= 0 && index < _images.Count && Accept(token) ? Results.File(_images[index], "image/jpeg") : Results.NotFound());

        return app;
    }

    /// <summary>Validates the token; valid requests count as activity and raise <see cref="RequestReceived"/>.</summary>
    private bool Accept(string token)
    {
        if (!HandoffToken.Matches(_token, token)) return false;
        Interlocked.Exchange(ref _lastActivityTicks, _time.GetUtcNow().UtcTicks);
        HasReceivedRequest = true;
        RequestReceived?.Invoke();
        return true;
    }

    private void CheckIdle()
    {
        var idle = _time.GetUtcNow().UtcTicks - Interlocked.Read(ref _lastActivityTicks);
        if (idle >= _idleTimeout.Ticks) _ = StopAsync(raiseStopped: true);
    }

    private async Task StopAsync(bool raiseStopped)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
        _idleTimer?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        if (raiseStopped) Stopped?.Invoke();
    }

    private static string LoadPage()
    {
        using var stream = typeof(HandoffServer).Assembly.GetManifestResourceStream("AstroPostMaster.Handoff.phone.html")
            ?? throw new InvalidOperationException("Embedded phone page is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
