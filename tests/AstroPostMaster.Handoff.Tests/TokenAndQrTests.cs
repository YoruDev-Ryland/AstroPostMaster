namespace AstroPostMaster.Handoff.Tests;

public class TokenAndQrTests
{
    [Fact]
    public void Token_IsUrlSafeAndUnique()
    {
        var a = HandoffToken.Create();
        var b = HandoffToken.Create();
        Assert.Equal(22, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Token_MatchesOnlyItself()
    {
        var t = HandoffToken.Create();
        Assert.True(HandoffToken.Matches(t, t));
        Assert.False(HandoffToken.Matches(t, t[..^1]));
        Assert.False(HandoffToken.Matches(t, null));
    }

    [Fact]
    public void QrCode_ProducesPng()
    {
        var png = QrCode.Png("https://192.168.1.50:48443/abc/");
        Assert.True(png.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }
}
