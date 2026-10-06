using System.Net;
using AstroPostMaster.Handoff;

namespace AstroPostMaster.App.Services;

public interface IHandoffSession : IAsyncDisposable
{
    Uri Url { get; }
    bool HasReceivedRequest { get; }
    /// <summary>Raised on a background thread.</summary>
    event Action? RequestReceived;
    /// <summary>Raised on a background thread when the server stopped itself after inactivity.</summary>
    event Action? Stopped;
}

public interface IHandoffHost
{
    IReadOnlyList<IPAddress> Addresses();
    Task<IHandoffSession> StartAsync(HandoffPackage package, IPAddress address, bool https, CancellationToken cancellationToken = default);
}

/// <summary>Real host: Kestrel <see cref="HandoffServer"/> with the persisted self-signed certificate.</summary>
public sealed class KestrelHandoffHost(AppState state) : IHandoffHost
{
    public IReadOnlyList<IPAddress> Addresses() => LanAddress.Best();

    public async Task<IHandoffSession> StartAsync(HandoffPackage package, IPAddress address, bool https, CancellationToken cancellationToken = default)
    {
        var certificate = https ? HandoffCertificate.LoadOrCreate(state.Store.Paths.Certificate, [address]) : null;
        var server = await HandoffServer.StartAsync(package, address, certificate, cancellationToken: cancellationToken);
        return new Session(server);
    }

    private sealed class Session : IHandoffSession
    {
        private readonly HandoffServer _server;

        public Session(HandoffServer server)
        {
            _server = server;
            server.RequestReceived += () => RequestReceived?.Invoke();
            server.Stopped += () => Stopped?.Invoke();
        }

        public Uri Url => _server.Url;
        public bool HasReceivedRequest => _server.HasReceivedRequest;
        public event Action? RequestReceived;
        public event Action? Stopped;
        public ValueTask DisposeAsync() => _server.DisposeAsync();
    }
}
