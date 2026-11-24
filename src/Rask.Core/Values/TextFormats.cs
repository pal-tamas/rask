using System.ComponentModel.DataAnnotations;

namespace Rask.Values;

// The format each kind of text is held to: an email address and a phone number exactly as [EmailAddress] and [Phone]
// check them (the attributes themselves ask, so the two paths never drift apart), a web address as an absolute http or
// https URI.
internal static class TextFormats
{
    private static readonly EmailAddressAttribute EmailRule = new();
    private static readonly PhoneAttribute PhoneRule = new();

    /// <summary>The rule a filled <paramref name="text" /> of <paramref name="kind" /> breaks, or null when its format is kept.</summary>
    public static ValueProblemKind? Refusal(TextKind kind, string text) =>
        kind switch
        {
            TextKind.Email when !EmailRule.IsValid(text) => ValueProblemKind.Email,
            TextKind.Phone when !PhoneRule.IsValid(text) => ValueProblemKind.Phone,
            TextKind.Url when !IsWebAddress(text) => ValueProblemKind.Url,
            _ => null,
        };

    private static bool IsWebAddress(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var address) &&
        (string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
         string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)) &&
        address.Host.Length > 0;
}
