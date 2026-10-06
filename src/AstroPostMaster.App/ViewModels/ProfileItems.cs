using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

/// <summary>Editable copies of profile records for the settings window.</summary>
public abstract partial class ProfileItem(string id, string name, bool isDefault) : ObservableObject
{
    public string Id { get; } = id;
    [ObservableProperty] public partial string Name { get; set; } = name;
    [ObservableProperty] public partial bool IsDefault { get; set; } = isDefault;

    protected string NameOr(string fallback) => string.IsNullOrWhiteSpace(Name) ? fallback : Name.Trim();

    protected static IReadOnlyList<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}

public sealed partial class SiteItem(Site site) : ProfileItem(site.Id, site.Name, site.IsDefault)
{
    [ObservableProperty] public partial int? Bortle { get; set; } = site.Bortle;
    public Site ToModel() => new(Id, NameOr("Untitled site"), Bortle is int b ? Math.Clamp(b, 1, 9) : null, IsDefault);
}

public sealed partial class RigItem(Rig rig) : ProfileItem(rig.Id, rig.Name, rig.IsDefault)
{
    [ObservableProperty] public partial string Scope { get; set; } = rig.Scope;
    [ObservableProperty] public partial string Camera { get; set; } = rig.Camera;
    [ObservableProperty] public partial string Filters { get; set; } = rig.Filters;
    [ObservableProperty] public partial string Mount { get; set; } = rig.Mount;
    /// <summary>Extra equipment lines, one per line (e.g. "Guider: ASI220mm Mini").</summary>
    [ObservableProperty] public partial string ExtraText { get; set; } = string.Join('\n', rig.Extra ?? []);
    public Rig ToModel() => new(Id, NameOr("Untitled rig"), Scope.Trim(), Camera.Trim(), Filters.Trim(), Mount.Trim(), Lines(ExtraText), IsDefault);
}

public sealed partial class SoftwareItem(SoftwareSet set) : ProfileItem(set.Id, set.Name, set.IsDefault)
{
    [ObservableProperty] public partial string LinesText { get; set; } = string.Join('\n', set.Lines ?? []);
    public SoftwareSet ToModel() => new(Id, NameOr("Untitled software"), Lines(LinesText), IsDefault);
}

public sealed partial class HashtagSetItem(HashtagSet set) : ProfileItem(set.Id, set.Name, set.IsDefault)
{
    [ObservableProperty] public partial string TagsText { get; set; } = string.Join(' ', set.Tags ?? []);
    public HashtagSet ToModel() => new(Id, NameOr("Untitled tags"), Hashtags.Split(TagsText), IsDefault);
}
