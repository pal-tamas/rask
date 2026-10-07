using System.Runtime.CompilerServices;

namespace Rask;

/// <summary>
///     Turns the element a caller hands to <see cref="UiModalTrigger" /> or <see cref="UiModalClose" /> into the
///     button that opens or closes a dialog.
/// </summary>
/// <remarks>
///     By HTML's invoker commands where the browser owns the dialog — both names on every control, so each
///     browser takes the one it has: <c>command</c> where invoker commands are supported, which the platform
///     acts on first, and <c>popovertarget</c> everywhere else. By a click handler where the page owns it.
/// </remarks>
internal static class UiModalInvoker
{
    // A component renders more than once with the children it was given, so an element is wired once and
    // told what to run each time after.
    private static readonly ConditionalWeakTable<Element, StrongBox<Callback>> Pressed = [];

    internal static (string, string?)[] Opens(string name) =>
        [("command", "show-modal"), ("commandfor", name), ("popovertarget", name)];

    internal static (string, string?)[] Closes(string name) =>
        [("command", "close"), ("commandfor", name), ("popovertarget", name), ("popovertargetaction", "hide")];

    /// <summary>Writes <paramref name="attributes" /> on the first element among <paramref name="children" />.</summary>
    /// <returns>Whether there was an element to write them on.</returns>
    internal static bool Wire(IEnumerable<Component?> children, params (string Name, string? Value)[] attributes)
    {
        if (First(children) is not { } invoker)
        {
            return false;
        }

        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in invoker.Attributes ?? merged)
        {
            merged[name] = value;
        }

        foreach (var (name, value) in attributes)
        {
            merged[name] = value;
        }

        invoker.Attributes = merged;

        return true;
    }

    /// <summary>
    ///     Makes a press of the first element among <paramref name="children" /> run <paramref name="then" />,
    ///     after any handler it already had: the runtime runs the nearest click handler and no other.
    /// </summary>
    /// <returns>Whether there was an element to press.</returns>
    internal static bool Press(IEnumerable<Component?> children, Callback then)
    {
        if (First(children) is not { } pressed)
        {
            return false;
        }

        if (Pressed.TryGetValue(pressed, out var wired))
        {
            wired.Value = then;

            return true;
        }

        var latest = new StrongBox<Callback>(then);
        var own = pressed.OnClick;
        Pressed.Add(pressed, latest);
        pressed.OnClick(async press =>
        {
            await own.Invoke(press).ConfigureAwait(true);

            await latest.Value.Invoke().ConfigureAwait(true);
        });

        return true;

    }

    // Depth first: a button inside a tooltip, or inside a fragment, is still the button.
    private static Element? First(IEnumerable<Component?>? children)
    {
        foreach (var child in children ?? [])
        {
            if (child is Element element)
            {
                return element;
            }

            if (First(child?.Children) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }
}
