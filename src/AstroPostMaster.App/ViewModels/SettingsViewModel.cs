using System.Collections.ObjectModel;
using AstroPostMaster.App.Services;
using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Handoff;
using AstroPostMaster.Handoff.Firewall;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroPostMaster.App.ViewModels;

/// <summary>Profiles and preferences, edited as copies and written back on <see cref="Save"/>.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Support link for the software itself. Shown only in the app UI — never in captions or images.</summary>
    public const string KoFiUrl = "https://ko-fi.com/rkremeier";

    private readonly AppState _state;
    private readonly IFirewall? _firewall;
    private readonly Func<IReadOnlyList<System.Net.IPAddress>> _addresses;
    private (System.Net.IPAddress Address, FirewallStatus Status)? _connection;

    public SettingsViewModel(AppState state, IFirewall? firewall = null, Func<IReadOnlyList<System.Net.IPAddress>>? addresses = null)
    {
        _state = state;
        _firewall = firewall;
        _addresses = addresses ?? LanAddress.Best;
        foreach (var s in state.Sites) Sites.Add(new SiteItem(s));
        foreach (var r in state.Rigs) Rigs.Add(new RigItem(r));
        foreach (var w in state.Software) Software.Add(new SoftwareItem(w));
        foreach (var h in state.HashtagSets) HashtagSets.Add(new HashtagSetItem(h));

        var s0 = state.Settings;
        CaptionTemplate = s0.CaptionTemplate;
        JpegQuality = s0.JpegQuality;
        ExportSize = s0.ExportSize;
        UseHttps = s0.UseHttps;
        LockOnExport = s0.LockOnExport;
        MoonLineEnabled = s0.MoonLineEnabled;
        Theme = s0.Theme;
        DefaultBrowseFolder = s0.DefaultBrowseFolder;
        WatermarkEnabled = s0.Watermark.Enabled;
        WatermarkText = s0.Watermark.Text;
        WatermarkCorner = s0.Watermark.Corner;
        WatermarkSizePercent = s0.Watermark.SizeFraction * 100;
        WatermarkOpacityPercent = s0.Watermark.Opacity * 100;

        SelectedSite = Sites.FirstOrDefault();
        SelectedRig = Rigs.FirstOrDefault();
        SelectedSoftware = Software.FirstOrDefault();
        SelectedHashtagSet = HashtagSets.FirstOrDefault();
    }

    public ObservableCollection<SiteItem> Sites { get; } = [];
    public ObservableCollection<RigItem> Rigs { get; } = [];
    public ObservableCollection<SoftwareItem> Software { get; } = [];
    public ObservableCollection<HashtagSetItem> HashtagSets { get; } = [];

    [ObservableProperty] public partial SiteItem? SelectedSite { get; set; }
    [ObservableProperty] public partial RigItem? SelectedRig { get; set; }
    [ObservableProperty] public partial SoftwareItem? SelectedSoftware { get; set; }
    [ObservableProperty] public partial HashtagSetItem? SelectedHashtagSet { get; set; }

    [ObservableProperty] public partial string CaptionTemplate { get; set; }
    [ObservableProperty] public partial ExportSize ExportSize { get; set; }
    public static IReadOnlyList<ExportSizeOption> ExportSizeOptions => ExportSizeOption.All;

    public int ExportSizeIndex
    {
        get => ExportSizeOption.IndexOf(ExportSize);
        set { if (value >= 0 && value < ExportSizeOption.All.Count) ExportSize = ExportSizeOption.All[value].Size; }
    }

    partial void OnExportSizeChanged(ExportSize value) => OnPropertyChanged(nameof(ExportSizeIndex));
    [ObservableProperty] public partial bool UseHttps { get; set; }
    [ObservableProperty] public partial bool LockOnExport { get; set; }
    [ObservableProperty] public partial bool MoonLineEnabled { get; set; }
    [ObservableProperty] public partial string Theme { get; set; }
    [ObservableProperty] public partial string? DefaultBrowseFolder { get; set; }
    [ObservableProperty] public partial bool WatermarkEnabled { get; set; }
    [ObservableProperty] public partial string WatermarkText { get; set; }
    [ObservableProperty] public partial Corner WatermarkCorner { get; set; }
    [ObservableProperty] public partial double WatermarkSizePercent { get; set; }
    [ObservableProperty] public partial double WatermarkOpacityPercent { get; set; }

    private int _jpegQuality;
    public int JpegQuality { get => _jpegQuality; set => SetProperty(ref _jpegQuality, Math.Clamp(value, 60, 100)); }

    public IReadOnlyList<string> Themes { get; } = ["Dark", "Light"];
    public IReadOnlyList<Corner> Corners { get; } = Enum.GetValues<Corner>();
    public IReadOnlyList<string> Placeholders => Core.Captions.CaptionTemplate.Placeholders;
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    public SiteItem AddSite() => AddTo(Sites, new SiteItem(new Site(Profiles.NewId(), "New site", null, Sites.Count == 0)), i => SelectedSite = i);
    public RigItem AddRig() => AddTo(Rigs, new RigItem(new Rig(Profiles.NewId(), "New rig", IsDefault: Rigs.Count == 0)), i => SelectedRig = i);
    public SoftwareItem AddSoftware() => AddTo(Software, new SoftwareItem(new SoftwareSet(Profiles.NewId(), "New software", null, Software.Count == 0)), i => SelectedSoftware = i);
    public HashtagSetItem AddHashtagSet() => AddTo(HashtagSets, new HashtagSetItem(new HashtagSet(Profiles.NewId(), "New tags", null, false)), i => SelectedHashtagSet = i);

    [RelayCommand] private void NewSite() => AddSite();
    [RelayCommand] private void NewRig() => AddRig();
    [RelayCommand] private void NewSoftware() => AddSoftware();
    [RelayCommand] private void NewHashtagSet() => AddHashtagSet();

    [RelayCommand] private void RemoveSite(SiteItem? item) => Remove(Sites, item, i => SelectedSite = i);
    [RelayCommand] private void RemoveRig(RigItem? item) => Remove(Rigs, item, i => SelectedRig = i);
    [RelayCommand] private void RemoveSoftware(SoftwareItem? item) => Remove(Software, item, i => SelectedSoftware = i);
    [RelayCommand] private void RemoveHashtagSet(HashtagSetItem? item) => Remove(HashtagSets, item, i => SelectedHashtagSet = i);

    [RelayCommand] private void SetDefaultSite(SiteItem? item) => SingleDefault(Sites, item);
    [RelayCommand] private void SetDefaultRig(RigItem? item) => SingleDefault(Rigs, item);
    [RelayCommand] private void SetDefaultSoftware(SoftwareItem? item) => SingleDefault(Software, item);

    [RelayCommand] private void ResetTemplate() => CaptionTemplate = DefaultCaptionTemplate.Text;

    // ---------- phone connection check ----------

    [ObservableProperty] public partial string? ConnectionStatus { get; private set; }
    [ObservableProperty] public partial string? ConnectionFixSummary { get; private set; }
    [ObservableProperty] public partial bool CanFixConnection { get; private set; }
    [ObservableProperty] public partial bool IsCheckingConnection { get; private set; }

    public bool CanCheckConnection => _firewall is not null;

    public async Task CheckPhoneConnectionAsync()
    {
        if (_firewall is null) return;
        IsCheckingConnection = true;
        try
        {
            var addresses = _addresses();
            if (addresses.Count == 0)
            {
                _connection = null;
                ShowConnection(null, "No network connection.");
                return;
            }
            var status = await _firewall.CheckAsync(HandoffServer.DefaultPort, addresses[0]);
            _connection = (addresses[0], status);
            ShowConnection(status, status.State == Reachability.Open ? "No issues found." : status.Message);
        }
        finally
        {
            IsCheckingConnection = false;
        }
    }

    public async Task FixConnectionAsync()
    {
        if (_firewall is null || _connection is not { Status.Fix: { } fix }) return;
        IsCheckingConnection = true;
        try
        {
            var result = await _firewall.FixAsync(fix);
            if (result.Outcome != FixOutcome.Applied)
            {
                ConnectionStatus = result.Message;
                return;
            }
        }
        finally
        {
            IsCheckingConnection = false;
        }
        await CheckPhoneConnectionAsync();
    }

    private void ShowConnection(FirewallStatus? status, string message)
    {
        ConnectionStatus = message;
        CanFixConnection = status is { Fix: not null } && status.State != Reachability.Open;
        ConnectionFixSummary = CanFixConnection ? status!.Fix!.Summary : null;
    }

    /// <summary>Writes profiles and settings to disk and updates the live app state in place.</summary>
    public void Save()
    {
        _state.IsReplacingProfiles = true;
        try
        {
            Replace(_state.Sites, Sites.Select(s => s.ToModel()));
            Replace(_state.Rigs, Rigs.Select(r => r.ToModel()));
            Replace(_state.Software, Software.Select(w => w.ToModel()));
            Replace(_state.HashtagSets, HashtagSets.Select(h => h.ToModel()));
        }
        finally
        {
            _state.IsReplacingProfiles = false;
        }

        var s = _state.Settings;
        s.CaptionTemplate = string.IsNullOrWhiteSpace(CaptionTemplate) ? DefaultCaptionTemplate.Text : CaptionTemplate;
        s.JpegQuality = JpegQuality;
        s.ExportSize = ExportSize;
        s.UseHttps = UseHttps;
        s.LockOnExport = LockOnExport;
        s.MoonLineEnabled = MoonLineEnabled;
        s.Theme = Theme;
        s.DefaultBrowseFolder = DefaultBrowseFolder;
        s.Watermark.Enabled = WatermarkEnabled;
        s.Watermark.Text = WatermarkText;
        s.Watermark.Corner = WatermarkCorner;
        s.Watermark.SizeFraction = Math.Clamp(WatermarkSizePercent, 1, 10) / 100;
        s.Watermark.Opacity = Math.Clamp(WatermarkOpacityPercent, 0, 100) / 100;

        _state.SaveProfiles();
        _state.SaveSettings();
    }

    private static T AddTo<T>(ObservableCollection<T> list, T item, Action<T> select)
    {
        list.Add(item);
        select(item);
        return item;
    }

    private static void Remove<T>(ObservableCollection<T> list, T? item, Action<T?> select) where T : class
    {
        if (item is null) return;
        var index = list.IndexOf(item);
        if (index < 0) return;
        list.RemoveAt(index);
        select(list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)]);
    }

    private static void SingleDefault<T>(IEnumerable<T> list, T? item) where T : ProfileItem
    {
        if (item is null) return;
        foreach (var other in list) other.IsDefault = ReferenceEquals(other, item);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
