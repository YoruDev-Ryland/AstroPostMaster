using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace AstroPostMaster.Handoff;

public static class HandoffToken
{
    /// <summary>128 random bits, base64url (22 characters).</summary>
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    public static bool Matches(string expected, string? candidate) =>
        candidate is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(candidate));
}
