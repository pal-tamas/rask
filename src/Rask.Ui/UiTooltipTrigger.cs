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
        trigger is HTMLButtonElement || (trigger is UiElement kit && string.Equals(kit.Tag, "button", StringComparison.Ordinal));

    /// <summary>Joins <paramref name="trigger" /> to the content, and makes it the invoker where it can be one.</summary>
    /// <param name="trigger">The tooltip's first child.</param>
    /// <param name="contentId">The id of the tooltip's content.</param>
    /// <param name="controls">Whether the trigger opens content of its own, rather than being described by it.</param>
    /// <param name="invoker"><c>popovertarget</c> or <c>interestfor</c>, or <see langword="null" /> for a tooltip that never shows.</param>
    internal static void Wire(Element trigger, string contentId, bool controls, string? invoker)
    {
        var aria = trigger.Aria is { } own
            ? new Dictionary<string, string?>(own, StringComparer.Ordinal)
            : new Dictionary<string, string?>(StringComparer.Ordinal);

        // Measured on Flux: a trigger with text of its own is described by the tooltip, and one without —
        // an icon button — is named by it.
        var named = aria.ContainsKey("labelledby") || HasText(trigger);
        var relation = Relation(controls, named);
        aria[relation] = Joined(aria.GetValueOrDefault(relation), contentId);
        trigger.Aria = aria;

        // An interest invoker is a button or a link; on anything else the stylesheet's :hover shows it.
        var takes = invoker switch
        {
            "popovertarget" => IsButton(trigger),
            "interestfor" => IsButton(trigger) || IsLink(trigger),
            _ => false,
        };
        if (!takes)
        {
            return;
        }

        var attributes = trigger.Attributes is { } extra
            ? new Dictionary<string, string?>(extra, StringComparer.Ordinal)
            : new Dictionary<string, string?>(StringComparer.Ordinal);
        attributes[invoker!] = contentId;
        trigger.Attributes = attributes;
    }

    private static string Relation(bool controls, bool named)
    {
        if (controls)
        {
            return "controls";
        }

        return named ? "describedby" : "labelledby";
    }

    private static bool IsLink(Element trigger) =>
        trigger is HTMLAnchorElement || (trigger is UiElement kit && string.Equals(kit.Tag, "a", StringComparison.Ordinal));

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
