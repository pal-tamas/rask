namespace Rask;

/// <summary>
///     Turns the element a caller hands to <see cref="UiModalTrigger" /> or <see cref="UiModalClose" /> into the
///     button that opens or closes a dialog.
/// </summary>
/// <remarks>
///     By HTML's invoker commands: <c>command</c> and <c>commandfor</c>, which the browser acts on with no
///     handler, and which the runtime acts on where the engine has none.
/// </remarks>
internal static class UiModalInvoker
{
    internal static (string, string?)[] Opens(string id) => [("command", "show-modal"), ("commandfor", id)];

    internal static (string, string?)[] Closes(string id) => [("command", "close"), ("commandfor", id)];

    /// <summary>A dismissal: the dialog raises <c>cancel</c>, then closes — what Escape does.</summary>
    internal static (string, string?)[] Dismisses(string id) => [("command", "request-close"), ("commandfor", id)];

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
