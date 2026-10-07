using Rask.Core.Components;

namespace Rask;

/// <summary>Writes onto a tooltip's trigger what joins it to the tooltip.</summary>
/// <remarks>
///     Flux does this in the browser, from its custom element. The kit ships no script, so it is done at
///     render, on the element the call site handed over — which is why the trigger has to BE one element.
/// </remarks>
internal static class UiTooltipTrigger
{
    /// <summary>Whether a click on it can open a popover: only a <c>&lt;button&gt;</c> is a <c>popovertarget</c>.</summary>
    internal static bool IsButton(Element trigger) =>
        trigger is HTMLButtonElement || string.Equals(KitTag(trigger), "button", StringComparison.Ordinal);

    /// <summary>Joins <paramref name="trigger" /> to the content, and makes it the invoker where it can be one.</summary>
    /// <param name="trigger">The tooltip's first child.</param>
    /// <param name="contentId">The id of the tooltip's content.</param>
    /// <param name="relation">How the trigger stands to the content.</param>
    /// <param name="opens">Whether a click may open it: a disabled tooltip keeps its ARIA and never shows.</param>
    internal static void Wire(Element trigger, string contentId, UiTooltipRelation relation, bool opens)
    {
        // A kit part that renders its element rather than being it carries that element's attributes itself.
        var wired = trigger is HostedElement { Owner: Element owner } ? owner : trigger;
        var aria = wired.Aria is { } own
            ? new Dictionary<string, string?>(own, StringComparer.Ordinal)
            : new Dictionary<string, string?>(StringComparer.Ordinal);

        // Measured on Flux: a trigger with text of its own is described by the tooltip, and one without —
        // an icon button — is named by it.
        var named = aria.ContainsKey("labelledby") || HasText(trigger);
        var name = Name(relation, named);
        aria[name] = Joined(aria.GetValueOrDefault(name), contentId);
        if (relation == UiTooltipRelation.Toggles)
        {
            aria["haspopup"] = "true";
        }

        // Flux writes the state beside `aria-controls`; the runtime keeps it true while the tooltip shows.
        if (relation == UiTooltipRelation.Controls)
        {
            aria["expanded"] = "false";
        }

        wired.Aria = aria;
        if (relation != UiTooltipRelation.Toggles || !opens)
        {
            return;
        }

        var attributes = wired.Attributes is { } extra
            ? new Dictionary<string, string?>(extra, StringComparer.Ordinal)
            : new Dictionary<string, string?>(StringComparer.Ordinal);
        attributes["popovertarget"] = contentId;
        wired.Attributes = attributes;
    }

    private static string Name(UiTooltipRelation relation, bool named)
    {
        if (relation != UiTooltipRelation.Describes)
        {
            return "controls";
        }

        return named ? "describedby" : "labelledby";
    }

    private static string? KitTag(Element trigger) => trigger switch
    {
        HostedElement hosted => hosted.Tag,
        UiElement kit => kit.Tag,
        _ => null,
    };

    // A kit component is rendered once per page render and wired each time, so the id may already be there.
    private static string Joined(string? ids, string id)
    {
        if (string.IsNullOrEmpty(ids))
        {
            return id;
        }

        return ids.Split(' ').Contains(id, StringComparer.Ordinal) ? ids : ids + " " + id;
    }

    private static bool HasText(Component part) =>
        part is Text text
            ? !string.IsNullOrWhiteSpace(text.Value)
            : part.Children?.Any(child => child is not null && HasText(child)) == true;
}
