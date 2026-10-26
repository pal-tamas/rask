namespace Rask.Core;

/// <summary>
///     Which field holds a secret the browser already has: its value goes browser to server and never back, except as
///     a clear, and what the browser sent for it is never written into a page (<see cref="Forms.HeardValues" />).
///     Asked of the values the element already resolved, so a plain input pays a few string compares.
/// </summary>
internal static class SecretField
{
    /// <summary>The marker the browser runtime reads: this field's value is not taken away when its attribute goes.</summary>
    internal const string Marker = "data-rask-secret";

    /// <summary>The <see cref="Marker" /> as an app sets it in an element's <c>data-*</c> bag.</summary>
    private const string MarkerKey = "rask-secret";

    private static readonly string[] CredentialTokens = ["current-password", "new-password", "one-time-code"];

    /// <summary>
    ///     A password, a field the browser fills as a credential or a code (an <c>autocomplete</c> token, in any case
    ///     and beside any others), or one the app marks <c>data-rask-secret</c>.
    /// </summary>
    /// <param name="type">The field's resolved <c>type</c>; null for a textarea.</param>
    /// <param name="autocomplete">Its <c>autocomplete</c> tokens.</param>
    /// <param name="data">Its <c>data-*</c> bag.</param>
    internal static bool Is(string? type, string? autocomplete, IReadOnlyDictionary<string, string?>? data = null) =>
        string.Equals(type, "password", StringComparison.OrdinalIgnoreCase)
        || NamesCredential(autocomplete)
        || MarkedByApp(data);

    /// <summary>The app already wrote <see cref="Marker" /> through the <c>data-*</c> bag, so the element must not again.</summary>
    internal static bool MarkedByApp(IReadOnlyDictionary<string, string?>? data) => data?.ContainsKey(MarkerKey) == true;

    /// <summary>The text the server may write for <paramref name="value" />: a clear, or nothing at all.</summary>
    internal static string? Writable(string? value) => string.IsNullOrEmpty(value) ? value : null;

    // autocomplete is a list of ASCII-case-insensitive tokens split on HTML whitespace; read without allocating.
    private static bool NamesCredential(string? autocomplete)
    {
        if (string.IsNullOrEmpty(autocomplete))
        {
            return false;
        }

        var tokens = autocomplete.AsSpan();
        foreach (var range in tokens.SplitAny(HtmlWhitespace))
        {
            var token = tokens[range];
            foreach (var credential in CredentialTokens)
            {
                if (token.Equals(credential, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private const string HtmlWhitespace = " \t\n\f\r";
}
