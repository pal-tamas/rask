using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools.Probe;

/// <summary>Which context values the devtools must never show.</summary>
/// <remarks>
///     The words the build redacts a prop by, applied to the context's name and its type's name — a context has no
///     attributes of its own to declare a secret with. The value of one that matches is never formatted, so its
///     <c>ToString</c> is never run.
/// </remarks>
internal static class DevToolsSensitive
{
    private static readonly string[] Words = ["password", "passcode", "secret", "token", "apikey", "credential"];

    // Matched whole, as the build does: as substrings they would redact Pinned, Spinner and Session.
    private static readonly string[] WholeNames = ["pin", "ssn"];

    internal static bool IsSensitive(Type valueType, string? name) =>
        Matches(DevToolsNames.Of(valueType)) || (name is not null && Matches(name));

    private static bool Matches(string text) =>
        Words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))
        || WholeNames.Any(whole => string.Equals(text, whole, StringComparison.OrdinalIgnoreCase));

    /// <summary>A provided value as the panel shows it: formatted like a prop, or withheld.</summary>
    internal static DevToolsProvidedContext Describe(in DevToolsProvideItem item) =>
        IsSensitive(item.ValueType, item.Name)
            ? new DevToolsProvidedContext(DevToolsNames.Of(item.ValueType), item.Name, PropsDescriber.Redacted, true)
            : new DevToolsProvidedContext(DevToolsNames.Of(item.ValueType), item.Name, PropsDescriber.Format(item.Value), false);
}
