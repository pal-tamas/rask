namespace Rask;

/// <summary>What makes a caller's element the button that opens a popover.</summary>
internal static class UiInvoker
{
    /// <summary>
    ///     Adds <c>popovertarget</c>, the ARIA that says what it opens and whether it is open, and Flux's
    ///     <c>data-open</c> styling hook, to an element the CALLER wrote — keeping whatever they put on it.
    /// </summary>
    internal static T Decorate<T>(T trigger, string panelId, string hasPopup, bool open)
        where T : Element
    {
        var aria = Copy(trigger.Aria);
        aria["haspopup"] = hasPopup;
        aria["expanded"] = open ? "true" : "false";
        aria["controls"] = panelId;

        var attributes = Copy(trigger.Attributes);
        attributes["popovertarget"] = panelId;

        // The element outlives a render, so `data-open` is taken off as deliberately as it is put on.
        var data = Copy(trigger.Data);
        if (open)
        {
            data["open"] = "";
        }
        else
        {
            data.Remove("open");
        }

        // Chain steps, not property writes: a chain-built element renders what its chain was given.
        return trigger.Aria(aria).Attributes(attributes).Data(data);
    }

    private static Dictionary<string, string?> Copy(IReadOnlyDictionary<string, string?>? source) =>
        source is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(source, StringComparer.Ordinal);
}
