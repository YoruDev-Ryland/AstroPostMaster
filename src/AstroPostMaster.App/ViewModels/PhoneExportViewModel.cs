using System.Net;
using AstroPostMaster.App.Services;
using AstroPostMaster.Handoff;
using AstroPostMaster.Handoff.Firewall;
using AstroPostMaster.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

/// <summary>Exports the post's slides, then serves them to a phone via QR code until closed.</summary>
public sealed partial class PhoneExportViewModel : ObservableObject
{
    public static readonly TimeSpan HelpDelay = TimeSpan.FromSeconds(60);

    private readonly EditorViewModel _editor;
    private readonly AppState _state;
    private readonly IUiDispatcher _ui;
    private readonly IHandoffHost _host;
    private readonly TimeProvider _time;
    private readonly IFirewall? _firewall;
    private (int Port, IPAddress Address)? _checked;
    private ExportResult? _export;
    private string _caption = "";
    private IHandoffSession? _session;
    private ITimer? _helpTimer;
    private bool _started;
    private int _generation;
    private bool _closed;

    public PhoneExportViewModel(EditorViewModel editor, AppState state, IUiDispatcher ui, IHandoffHost host, TimeProvider? time = null,
        IFirewall? firewall = null)
    {
        _firewall = firewall;
        _editor = editor;
        _state = state;
        _ui = ui;
        _host = host;
        _time = time ?? TimeProvider.System;
        Status = "Preparing slides…";
    }

    public string Title => _editor.Post.Title;
    public IReadOnlyList<string> ExportedFiles => _export?.SlideFiles ?? [];

    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; }
    [ObservableProperty] public partial bool HasError { get; private set; }
    [ObservableProperty] public partial bool IsExporting { get; private set; }
    [ObservableProperty] public partial Uri? Url { get; private set; }
    [ObservableProperty] public partial byte[]? QrPng { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<IPAddress> Addresses { get; private set; } = [];
    [ObservableProperty] public partial IPAddress? SelectedAddress { get; set; }
    [ObservableProperty] public partial bool ShowConnectionHelp { get; private set; }
    [ObservableProperty] public partial bool PhoneConnected { get; private set; }

    public bool HasMultipleAddresses => Addresses.Count > 1;

    // ---------- firewall self-check ----------

    [ObservableProperty] public partial FirewallStatus? Firewall { get; private set; }
    [ObservableProperty] public partial string? FirewallNote { get; private set; }
    [ObservableProperty] public partial bool IsFixingFirewall { get; private set; }

    public bool FirewallBlocked => Firewall?.State == Reachability.Blocked;
    public bool ShowFirewallCard => FirewallBlocked || FirewallUncertain || FirewallNote is not null;
    public bool FirewallUncertain => Firewall is { State: Reachability.Unknown, Fix: not null };
    public bool CanFixFirewall => Firewall is { Fix: not null } f && f.State != Reachability.Open;
    public string? FirewallMessage => Firewall?.Message;
    public string? FirewallFixSummary => Firewall?.Fix?.Summary;
    public string? FirewallManualCommand => Firewall?.Fix?.ManualCommand;

    /// <summary>Completes when the latest firewall check has finished.</summary>
    public Task FirewallChecking { get; private set; } = Task.CompletedTask;

    partial void OnFirewallNoteChanged(string? value) => OnPropertyChanged(nameof(ShowFirewallCard));

    partial void OnFirewallChanged(FirewallStatus? value)
    {
        OnPropertyChanged(nameof(ShowFirewallCard));
        OnPropertyChanged(nameof(FirewallBlocked));
        OnPropertyChanged(nameof(FirewallUncertain));
        OnPropertyChanged(nameof(CanFixFirewall));
        OnPropertyChanged(nameof(FirewallMessage));
        OnPropertyChanged(nameof(FirewallFixSummary));
        OnPropertyChanged(nameof(FirewallManualCommand));
    }

    /// <summary>Applies the offered firewall change (the OS asks for the password/admin consent), then checks again.</summary>
    public async Task FixFirewallAsync()
    {
        if (_firewall is null || Firewall?.Fix is not { } fix || _checked is not { } target) return;
        IsFixingFirewall = true;
        try
        {
            var result = await _firewall.FixAsync(fix);
            FirewallNote = result.Message;
            if (result.Outcome != FixOutcome.Applied) return;
            await CheckFirewallAsync(target.Port, target.Address);
            if (Firewall?.State == Reachability.Open) FirewallNote = null;
        }
        finally
        {
            IsFixingFirewall = false;
        }
    }

    private async Task CheckFirewallAsync(int port, IPAddress address)
    {
        if (_firewall is null) return;
        _checked = (port, address);
        try
        {
            var status = await _firewall.CheckAsync(port, address);
            _ui.Post(() => Firewall = status);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // A failed check is not an error for the user; the connection help still applies.
        }
    }

    /// <summary>Completes when a session restart triggered by <see cref="SelectedAddress"/> has finished.</summary>
    public Task Restarting { get; private set; } = Task.CompletedTask;

    partial void OnAddressesChanged(IReadOnlyList<IPAddress> value) => OnPropertyChanged(nameof(HasMultipleAddresses));

    partial void OnSelectedAddressChanged(IPAddress? value)
    {
        if (_started && value is not null && !_closed) Restarting = RestartAfterAsync(Restarting, value);
    }

    public async Task StartAsync()
    {
        if (!await EnsureExportedAsync()) return;

        var addresses = _host.Addresses();
        Addresses = addresses;
        if (addresses.Count == 0)
        {
            Fail("No network connection.");
            return;
        }

        SelectedAddress = addresses[0];
        await StartSessionAsync(addresses[0]);
        _started = true;
    }

    /// <summary>Exports (if needed) and copies the slides and caption into a titled subfolder. Returns it, or null on failure.</summary>
    public async Task<string?> ExportToFolderAsync(string folder)
    {
        if (!await EnsureExportedAsync()) return null;
        try
        {
            var target = PostExporter.CopyTo(_export!, folder, _editor.Post.Title);
            Status = $"Saved to {target}";
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(ex.Message);
            return null;
        }
    }

    public async Task CloseAsync()
    {
        _closed = true;
        _helpTimer?.Dispose();
        _helpTimer = null;
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }
    }

    private async Task<bool> EnsureExportedAsync()
    {
        if (_export is not null) return true;
        await _editor.FlushAsync();
        _caption = _editor.CaptionText;
        var post = _editor.Post;
        var settings = _state.Settings;
        var exportDir = _state.Store.Paths.ExportDir(post.Id);
        var progress = new ProgressReporter(p => _ui.Post(() => Progress = p));

        IsExporting = true;
        Status = "Preparing slides…";
        try
        {
            _export = await Task.Run(() => PostExporter.Export(post, _caption, settings, exportDir, progress));
            Progress = 1;
            return true;
        }
        catch (FileNotFoundException)
        {
            Fail($"Source image not found: {post.SourcePath}");
        }
        catch (Exception ex) when (ex is ImageLoadException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Fail(ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
        return false;
    }

    /// <summary>Restarts strictly one after another, so overlapping address changes can't orphan a server.</summary>
    private async Task RestartAfterAsync(Task previous, IPAddress address)
    {
        try { await previous; } catch { /* reported by the previous attempt */ }
        if (!_closed && Equals(address, SelectedAddress)) await StartSessionAsync(address);
    }

    private async Task StartSessionAsync(IPAddress address)
    {
        var generation = ++_generation;
        _helpTimer?.Dispose();
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        try
        {
            var package = new HandoffPackage(_editor.Post.Title, _caption, _export!.SlideFiles);
            var session = await _host.StartAsync(package, address, _state.Settings.UseHttps);
            if (_closed || generation != _generation)
            {
                await session.DisposeAsync();
                return;
            }
            _session = session;
            session.RequestReceived += () => _ui.Post(OnPhoneRequest);
            session.Stopped += () => _ui.Post(() =>
            {
                Url = null;
                QrPng = null;
                Status = "Stopped after 15 idle minutes.";
            });

            Url = session.Url;
            QrPng = QrCode.Png(session.Url.ToString());
            FirewallChecking = CheckFirewallAsync(session.Url.Port, address);
            PhoneConnected = false;
            ShowConnectionHelp = false;
            HasError = false;
            Status = "Scan with your phone.";
            _helpTimer = _time.CreateTimer(_ => _ui.Post(() => { if (!PhoneConnected && !_closed) ShowConnectionHelp = true; }),
                null, HelpDelay, Timeout.InfiniteTimeSpan);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException
                                       or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        {
            Fail($"Couldn't start the phone server: {ex.Message}");
        }
    }

    private void OnPhoneRequest()
    {
        if (PhoneConnected) return;
        PhoneConnected = true;
        ShowConnectionHelp = false;
        Status = "Phone connected.";
    }

    private void Fail(string message)
    {
        HasError = true;
        Status = message;
    }

    private sealed class ProgressReporter(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
