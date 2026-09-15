using Rask.Core;
using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools.Probe;

/// <summary>A context value a component provides to everything rendered inside it, as the panel shows it.</summary>
/// <param name="Type">The type it is provided as — the key readers match on.</param>
/// <param name="Name">The name that tells two providers of one type apart, when it has one.</param>
/// <param name="Value">The value, formatted like a prop, or null.</param>
/// <param name="IsRedacted">Whether the value was withheld: its name or type says it is a secret.</param>
internal sealed record DevToolsProvidedContext(string Type, string? Name, string? Value, bool IsRedacted);

/// <summary>A context value a component read while it rendered, and where it came from.</summary>
/// <param name="Type">The type it asked for.</param>
/// <param name="Name">The name it asked by, when it gave one.</param>
/// <param name="Found">Whether a provider answered. A read nothing answered is still a read: <c>Has</c> asked and got no.</param>
/// <param name="ProviderId">The tree id of the component whose markup provided it, or null.</param>
/// <param name="ProviderType">That component's name, or null.</param>
internal sealed record DevToolsReadContext(string Type, string? Name, bool Found, long? ProviderId, string? ProviderType);

/// <summary>One provide as the walk met it: the component whose markup holds the provider, and what it provided.</summary>
internal readonly record struct DevToolsProvideItem(Component Owner, Type ValueType, string? Name, object? Value);

/// <summary>One read as the walk met it: the component that read, what it asked for, and whose provider answered.</summary>
internal readonly record struct DevToolsReadItem(
    Component Reader, Type Requested, string? Name, bool Found, Component? Provider);

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

    private static bool Matches(string text)
    {
        foreach (var word in Words)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var whole in WholeNames)
        {
            if (string.Equals(text, whole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A provided value as the panel shows it: formatted like a prop, or withheld.</summary>
    internal static DevToolsProvidedContext Describe(in DevToolsProvideItem item) =>
        IsSensitive(item.ValueType, item.Name)
            ? new DevToolsProvidedContext(DevToolsNames.Of(item.ValueType), item.Name, PropsDescriber.Redacted, true)
            : new DevToolsProvidedContext(DevToolsNames.Of(item.ValueType), item.Name, PropsDescriber.Format(item.Value), false);
}
