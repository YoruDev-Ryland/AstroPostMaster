using System.Collections.ObjectModel;
using System.Globalization;
using AstroPostMaster.App.Services;
using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Catalog;
using AstroPostMaster.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

/// <summary>Edits a post's <see cref="CaptionInput"/> and keeps a live preview of the rendered caption.</summary>
public sealed partial class CaptionViewModel : ObservableObject
{
    private static readonly (string Key, string Label)[] Broadband = [("L", "L"), ("R", "R"), ("G", "G"), ("B", "B")];
    private static readonly (string Key, string Label)[] Narrowband = [("Ha", "Hα"), ("OIII", "OIII"), ("SII", "SII")];

    private readonly CaptionInput _input;
    private readonly AppState _state;
    private readonly Action _changed;
    private readonly Func<bool> _isLocked;

    public CaptionViewModel(CaptionInput input, AppState state, Action changed, Func<bool>? isLocked = null)
    {
        _input = input;
        _state = state;
        _changed = changed;
        _isLocked = isLocked ?? (() => false);

        BroadbandChips = Broadband.Select(b => new SelectableItem(b.Key, b.Label, input.Broadband.Contains(b.Key), OnBroadbandToggled)).ToList();
        NarrowbandChips = Narrowband.Select(n => new SelectableItem(n.Key, n.Label, PaletteHas(input.Palette, n.Key), OnNarrowbandToggled)).ToList();
        foreach (var row in input.IntegrationRows) IntegrationRows.Add(new IntegrationRowViewModel(row, RowsChanged));
        _datesText = string.Join(", ", input.Dates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        _extraHashtagsText = string.Join(' ', input.ExtraHashtags);
        HashtagSets = BuildHashtagSets();
        Preview = Build();
    }

    [ObservableProperty] public partial string Preview { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<SelectableItem> HashtagSets { get; private set; }
    [ObservableProperty] public partial bool DatesInvalid { get; private set; }

    public IReadOnlyList<SelectableItem> BroadbandChips { get; }
    public IReadOnlyList<SelectableItem> NarrowbandChips { get; }
    public ObservableCollection<IntegrationRowViewModel> IntegrationRows { get; } = [];
    public IReadOnlyList<string> PalettePresets => FilterString.PalettePresets;
    /// <summary>Presets plus the current palette when it isn't one (e.g. "H"), so a bound ComboBox can show it.</summary>
    public IReadOnlyList<string> PaletteOptions =>
        _input.Palette is { } p && !FilterString.PalettePresets.Contains(p) ? [p, .. FilterString.PalettePresets] : FilterString.PalettePresets;
    public ObservableCollection<Site> Sites => _state.Sites;
    public ObservableCollection<Rig> Rigs => _state.Rigs;
    public ObservableCollection<SoftwareSet> SoftwareSets => _state.Software;

    // ---------- target ----------

    /// <summary>True (and the UI is told to re-read <paramref name="property"/>) when the post is locked.</summary>
    private bool Rejected(string property)
    {
        if (!_isLocked()) return false;
        OnPropertyChanged(property);
        return true;
    }

    public string TargetText
    {
        get => _input.TargetText;
        set
        {
            if (_input.TargetText == value || Rejected(nameof(TargetText))) return;
            _input.TargetText = value;
            _input.TargetCatalogId = null;
            Notify(nameof(TargetText));
        }
    }

    public IReadOnlyList<CatalogEntry> Search(string text) => _state.Catalog.Search(text, 12);

    /// <summary>Autocomplete items; <see cref="TargetSuggestion.ToString"/> is the display text the box writes back.</summary>
    public IReadOnlyList<TargetSuggestion> Suggest(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : Search(text).Select(e => new TargetSuggestion(e)).ToList();

    public void SelectTarget(CatalogEntry entry)
    {
        if (Rejected(nameof(TargetText))) return;
        _input.TargetText = TargetCatalog.Display(entry);
        _input.TargetCatalogId = entry.PrimaryId;
        Notify(nameof(TargetText));
    }

    /// <summary>
    /// Fills the target from a file name when the target is still empty. Handles exact names ("M31",
    /// "Thor's Helmet"), trailing qualifiers ("Heart Nebula-1102250103", "Crescent Nebula_Square", "Lagoon2")
    /// and unambiguous name prefixes of 6+ characters ("Andromeda").
    /// </summary>
    public void TryAutoTarget(string fileStem)
    {
        if (!string.IsNullOrWhiteSpace(_input.TargetText)) return;
        if (GuessTarget(fileStem) is { } entry) SelectTarget(entry);
    }

    private CatalogEntry? GuessTarget(string stem)
    {
        var candidates = new List<string> { stem.Trim() };
        for (var i = 0; i < 2; i++)
        {
            var shorter = TrailingQualifier().Replace(candidates[^1], "").Trim();
            if (shorter.Length == 0 || shorter == candidates[^1]) break;
            candidates.Add(shorter);
        }

        foreach (var candidate in candidates)
            if (_state.Catalog.Find(candidate) is { } exact) return exact;

        foreach (var candidate in candidates.Where(c => TargetCatalog.Key(c).Length >= 6))
            if (_state.Catalog.Search(candidate, 1) is [var first]) return first;

        return null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"([\s_\-]+[^\s_\-]*|(?<=\p{L})\d+)$")]
    private static partial System.Text.RegularExpressions.Regex TrailingQualifier();

    public string Description
    {
        get => _input.Description;
        set { if (_input.Description != value && !Rejected(nameof(Description))) { _input.Description = value; Notify(nameof(Description)); } }
    }

    // ---------- profiles ----------

    public Site? Site
    {
        get => Profiles.ById(_state.Sites, _input.SiteId);
        set { if (!_state.IsReplacingProfiles && _input.SiteId != value?.Id && !Rejected(nameof(Site))) { _input.SiteId = value?.Id; Notify(nameof(Site)); } }
    }

    public Rig? Rig
    {
        get => Profiles.ById(_state.Rigs, _input.RigId);
        set { if (!_state.IsReplacingProfiles && _input.RigId != value?.Id && !Rejected(nameof(Rig))) { _input.RigId = value?.Id; Notify(nameof(Rig)); } }
    }

    public SoftwareSet? Software
    {
        get => Profiles.ById(_state.Software, _input.SoftwareId);
        set { if (!_state.IsReplacingProfiles && _input.SoftwareId != value?.Id && !Rejected(nameof(Software))) { _input.SoftwareId = value?.Id; Notify(nameof(Software)); } }
    }

    // ---------- filters ----------

    public string? Palette
    {
        get => _input.Palette;
        set
        {
            // A ComboBox writes null when its selection leaves ItemsSource; palettes are cleared via the chips instead.
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = value.Trim().ToUpperInvariant();
            if (_input.Palette == normalized || Rejected(nameof(Palette))) return;
            _input.Palette = normalized;
            Notify(nameof(Palette), nameof(PaletteOptions), nameof(FilterText));
        }
    }

    /// <summary>The filter string shown in the caption. Typing replaces it; clearing returns to the chip-generated value.</summary>
    public string FilterText
    {
        get => CaptionBuilder.ResolveFilters(_input);
        set
        {
            var overrideText = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_input.FilterOverride == overrideText || Rejected(nameof(FilterText))) return;
            _input.FilterOverride = overrideText;
            Notify(nameof(FilterText));
        }
    }

    private void OnBroadbandToggled(SelectableItem chip)
    {
        if (_isLocked()) { chip.Revert(!chip.IsSelected); return; }
        _input.Broadband = BroadbandChips.Where(c => c.IsSelected).Select(c => c.Key).ToList();
        Notify(nameof(FilterText));
    }

    private void OnNarrowbandToggled(SelectableItem chip)
    {
        if (_isLocked()) { chip.Revert(!chip.IsSelected); return; }
        bool On(string key) => NarrowbandChips.Single(c => c.Key == key).IsSelected;
        var palette = FilterString.DefaultPalette(On("Ha"), On("OIII"), On("SII"));
        _input.Palette = palette.Length == 0 ? null : palette;
        Notify(nameof(PaletteOptions), nameof(Palette), nameof(FilterText));
    }

    private static bool PaletteHas(string? palette, string key) => palette is not null && key switch
    {
        "Ha" => palette.Contains('H'),
        "OIII" => palette.Contains('O'),
        "SII" => palette.Contains('S'),
        _ => false,
    };

    // ---------- integration ----------

    public string IntegrationText
    {
        get => _input.IntegrationText;
        set { if (_input.IntegrationText != value && !Rejected(nameof(IntegrationText))) { _input.IntegrationText = value; Notify(nameof(IntegrationText), nameof(IntegrationTotal)); } }
    }

    public string IntegrationTotal => CaptionBuilder.ResolveIntegration(_input);

    /// <summary>Shown in the empty integration box: the per-filter total when rows exist, else an example.</summary>
    public string IntegrationPlaceholder => _input.IntegrationRows.Count > 0 ? IntegrationTotal : "12h30m";

    public IntegrationRowViewModel AddIntegrationRow()
    {
        var row = new IntegrationRowViewModel(new IntegrationRow("", 0, 300), RowsChanged);
        if (_isLocked()) return row; // detached: never reaches the post
        IntegrationRows.Add(row);
        RowsChanged();
        return row;
    }

    public void RemoveIntegrationRow(IntegrationRowViewModel row)
    {
        if (!_isLocked() && IntegrationRows.Remove(row)) RowsChanged();
    }

    private void RowsChanged()
    {
        if (_isLocked()) return;
        _input.IntegrationRows = IntegrationRows.Select(r => r.ToModel()).Where(r => r.Subs > 0 && r.SubSeconds > 0).ToList();
        Notify(nameof(IntegrationTotal), nameof(IntegrationPlaceholder));
    }

    // ---------- dates ----------

    private string _datesText;

    /// <summary>Imaging nights as yyyy-MM-dd separated by commas or spaces.</summary>
    public string DatesText
    {
        get => _datesText;
        set
        {
            if (Rejected(nameof(DatesText)) || !SetProperty(ref _datesText, value)) return;
            var valid = new List<DateOnly>();
            var invalid = false;
            foreach (var token in value.Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) valid.Add(d);
                else invalid = true;
            }
            DatesInvalid = invalid;
            _input.Dates = valid.Distinct().Order().ToList();
            Notify();
        }
    }

    // ---------- hashtags ----------

    private string _extraHashtagsText;

    public string ExtraHashtagsText
    {
        get => _extraHashtagsText;
        set
        {
            if (Rejected(nameof(ExtraHashtagsText)) || !SetProperty(ref _extraHashtagsText, value)) return;
            _input.ExtraHashtags = Hashtags.Split(value).ToList();
            Notify();
        }
    }

    private IReadOnlyList<SelectableItem> BuildHashtagSets() =>
        _state.HashtagSets.Select(s => new SelectableItem(s.Id, s.Name, _input.HashtagSetIds.Contains(s.Id), OnHashtagSetToggled)).ToList();

    private void OnHashtagSetToggled(SelectableItem set)
    {
        if (_isLocked()) { set.Revert(!set.IsSelected); return; }
        _input.HashtagSetIds = HashtagSets.Where(s => s.IsSelected).Select(s => s.Key).ToList();
        Notify();
    }

    // ---------- plumbing ----------

    /// <summary>Re-reads profiles and settings after they were edited elsewhere.</summary>
    public void Reload()
    {
        HashtagSets = BuildHashtagSets();
        OnPropertyChanged(nameof(Site));
        OnPropertyChanged(nameof(Rig));
        OnPropertyChanged(nameof(Software));
        Refresh();
    }

    public void Refresh() => Preview = Build();

    private void Notify(params string[] properties)
    {
        foreach (var p in properties) OnPropertyChanged(p);
        Preview = Build();
        _changed();
    }

    private string Build() => CaptionBuilder.Build(_input, _state.Sources(), _state.Settings);
}

public sealed record TargetSuggestion(CatalogEntry Entry)
{
    public string Display => TargetCatalog.Display(Entry);
    public string Detail => string.Join(" · ", Entry.Ids.Skip(1).Take(3));
    public override string ToString() => Display;
}
