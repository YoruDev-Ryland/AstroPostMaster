namespace AstroPostMaster.Core.Captions;

public static class DefaultCaptionTemplate
{
    public const string Text = """
        {target.display}

        {description}

        Acquisition:
        {site}
        Filters: {filters}
        Integration: {integration}
        {integration.breakdown}
        Dates: {dates}
        Moon: {moon}

        Equipment:
        Scope: {rig.scope}
        Camera: {rig.camera}
        Filters: {rig.filters}
        Mount: {rig.mount}
        {rig.extra}

        Software:
        {software}

        {hashtags}
        """;
}
