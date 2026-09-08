using System.Text.Json;

namespace Rask.Core.Live;

// The typed payload for OnToggle/OnBeforeToggle on Element — the open-state transition of a popover
// or a <details>.
//
// It exists because the state belongs to the BROWSER. A [popover] closes itself on Escape and on a
// click outside, and nothing told C# about it: a component tracking its own open flag went on
// believing the panel was open, and its aria-expanded went on saying so over a closed list. That is
// the one gap between "the browser owns dismissal" and "C# owns the state", and it is why every
// C#-driven popover needed this before it could keep an accurate ARIA contract.
//
// OldState/NewState are the platform's own words — "closed" and "open" — passed through rather than
// translated to a bool. A bool would read better in C# and would be a lie the first time the platform
// adds a third state; the values are what the DOM event carries, and a caller comparing against
// "open" is comparing against the spec rather than against our interpretation of it.
public sealed record ToggleEventArgs(string OldState, string NewState)
{
    /// <summary>Whether the element is open after this transition.</summary>
    /// <remarks>
    ///     The convenience nobody should have to write themselves, and the reason it is a property
    ///     rather than a second constructor parameter: it is derived, so it cannot disagree with
    ///     <see cref="NewState" />.
    /// </remarks>
    public bool IsOpen => string.Equals(NewState, "open", StringComparison.Ordinal);

    internal static ToggleEventArgs FromJson(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new ToggleEventArgs("", "");
        }

        return new ToggleEventArgs(ReadString(payload, "oldState"), ReadString(payload, "newState"));
    }

    private static string ReadString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
