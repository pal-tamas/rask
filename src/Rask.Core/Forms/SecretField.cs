namespace Rask.Core;

/// <summary>
///     Which input holds a secret the browser already has: its value goes browser to server and never back, except as
///     a clear. Asked of the values the element already resolved, so a plain input pays a few string compares.
/// </summary>
internal static class SecretField
{
    /// <summary>The marker the browser runtime reads: this field's value is not taken away when its attribute goes.</summary>
    internal const string Marker = "data-rask-secret";

    internal static bool Is(string? type, string? autocomplete) =>
        string.Equals(type, "password", StringComparison.OrdinalIgnoreCase)
        || autocomplete is "current-password" or "new-password" or "one-time-code";

    /// <summary>The text the server may write for <paramref name="value" />: a clear, or nothing at all.</summary>
    internal static string? Writable(string? value) => string.IsNullOrEmpty(value) ? value : null;
}
