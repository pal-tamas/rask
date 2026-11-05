using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Rask.Server;

/// <summary>
///     What a kept revalidator's answer is filed under: the principal's claims as a short hash, so two principals share
///     an answer only when everything the answer can depend on is the same.
/// </summary>
/// <remarks>
///     Every claim counts — its type, value and issuer — and the identity's authentication type, except
///     <c>iat</c> and <c>nbf</c>, which a token issued again changes and no answer reads. <c>exp</c>, a chosen tenant,
///     a sign-in's id all count: a principal that differs in them is another question.
/// </remarks>
internal static class PrincipalFingerprint
{
    /// <summary>The hash of <paramref name="principal" />'s claims, in any order they were added.</summary>
    public static string Of(ClaimsPrincipal principal)
    {
        var parts = new List<string>();
        foreach (var identity in principal.Identities)
        {
            parts.Add("\u0001" + identity.AuthenticationType);
            parts.AddRange(identity.Claims.Where(static c => !IsVolatile(c.Type))
                .Select(static c => Field(c.Type) + Field(c.Value) + Field(c.Issuer)));
        }

        parts.Sort(StringComparer.Ordinal);

        var buffer = new ArrayBufferWriter<byte>();
        foreach (var part in parts)
        {
            Encoding.UTF8.GetBytes(part + "\u0000", buffer);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }

    // Length-prefixed, so no value can pass for two fields.
    private static string Field(string value) => value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;

    private static bool IsVolatile(string type) => type is "iat" or "nbf";
}
