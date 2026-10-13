namespace Rask;

/// <summary>What makes a caller's element the button that opens a popover.</summary>
internal static class UiInvoker
{
    /// <summary>
    ///     Adds <c>popovertarget</c>, the ARIA that says what it opens and whether it is open, and the request
    ///     for Flux's <c>data-open</c> styling hook, to an element the CALLER wrote — keeping whatever they put on it.
    /// </summary>
    /// <remarks>
    ///     <paramref name="open" /> is what the PAGE said, for the first paint. From there on both marks are the
    ///     runtime's: it writes <c>aria-expanded</c> and <c>data-open</c> in the task the browser opens or closes
    ///     the panel in (rask-toggle.ts), and no render is asked for.
    /// </remarks>
    internal static T Decorate<T>(T trigger, string panelId, string hasPopup, bool open)
        where T : Element
    {
        var aria = Copy(trigger.Aria);
        aria["haspopup"] = hasPopup;
        aria["expanded"] = open ? "true" : "false";
        aria["controls"] = panelId;

        var attributes = Copy(trigger.Attributes);
        attributes["popovertarget"] = panelId;

        var data = Copy(trigger.Data);
        data[OpenMark] = panelId;

        // Chain steps, not property writes: a chain-built element renders what its chain was given.
        return trigger.Aria(aria).Attributes(attributes).Data(data);
    }

    /// <summary>Asks the runtime for <c>data-open</c> on the element while the popover with this id shows.</summary>
    internal const string OpenMark = "rask-open-mark";

    private static Dictionary<string, string?> Copy(IReadOnlyDictionary<string, string?>? source) =>
        source is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(source, StringComparer.Ordinal);
}
