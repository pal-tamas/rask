using System.Globalization;

namespace Rask;

/// <summary>
///     Flux's <c>flux:tooltip</c>: a line of help shown beside its first child while that child is hovered
///     or has keyboard focus.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Tooltip.Content("Settings")[Ui.Button…]</c>. The first child is the trigger; what it shows is
///     <see cref="Content" />, or a <see cref="UiTooltipContent" /> written after the trigger when it is more
///     than a line of text.
///     </para>
///     <para>
///     No script. The content is a <c>[popover]</c> placed by CSS anchor positioning, which also flips it to
///     the other side at the viewport's edge. A trigger that is a button or a link shows it as an interest
///     invoker (<c>interestfor</c>), so it rides in the top layer and Escape dismisses it; anywhere else,
///     and in an engine without interest invokers, <c>:hover</c> and <c>:focus-visible</c> show it in place.
///     <see cref="Toggleable" /> makes the trigger a <c>popovertarget</c>: a click opens it, and a click
///     outside or Escape closes it — the only tooltip a touch screen ever shows.
///     </para>
///     <para>
///     The trigger is told about it, which is what makes a screen reader say it: a trigger with text of its
///     own is <c>aria-describedby</c> the tooltip, and one without — an icon button — is
///     <c>aria-labelledby</c> it, so the tooltip is its name. That needs the trigger to be one element (an
///     HTML element or a kit component that is one, such as <c>Ui.Button</c>); around anything else the
///     tooltip's own wrapper carries the description as a group.
///     </para>
/// </remarks>
public sealed partial class UiTooltip : Component
{
    private readonly int _instance = UiInstanceCounter.Next();

    /// <summary>The text it shows. For anything richer, write a <see cref="UiTooltipContent" /> child instead.</summary>
    public string? Content { get; set; }

    /// <summary>Which side of the trigger it opens on. Above, unless this says otherwise.</summary>
    public Ui.TooltipPosition? Position { get; set; }

    /// <summary>Where along that side it sits. Centred, unless this says otherwise.</summary>
    public Ui.TooltipAlign? Align { get; set; }

    /// <summary>Stops it showing. The trigger keeps its description.</summary>
    public bool? Disabled { get; set; }

    /// <summary>The distance between the trigger and the tooltip, in pixels. 5 when unset.</summary>
    public int? Gap { get; set; }

    /// <summary>How far it is slid along its side, in pixels, away from the edge it is aligned to.</summary>
    public int? Offset { get; set; }

    /// <summary>
    ///     Opens it on a click rather than a hover, for content that has to reach a touch screen, where
    ///     nothing hovers.
    /// </summary>
    public bool? Toggleable { get; set; }

    /// <summary>
    ///     Says the content is more than a description — it holds something to read at length or act on — so
    ///     the trigger is wired as the control of it (<c>aria-controls</c>) rather than described by it, and
    ///     the content stays in the reading order.
    /// </summary>
    public bool? Interactive { get; set; }

    /// <summary>A keyboard shortcut shown after <see cref="Content" /> — <c>"⌘S"</c>.</summary>
    public string? Kbd { get; set; }

    /// <summary>Classes for the wrapper around the trigger.</summary>
    public string? Class { get; set; }

    private string ContentId => "ui-tooltip-" + _instance.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var parts = (Children ?? []).Where(child => child is not null).ToList();
        var trigger = parts.Find(child => child is not UiTooltipContent) as Element;
        var toggled = Toggleable == true;
        var clicks = toggled && trigger is not null && UiTooltipTrigger.IsButton(trigger);
        var controls = clicks || Interactive == true;
        var root = Div.Class("inline-flex", Class).Data(Marks(toggled));

        if (trigger is not null && toggled == clicks)
        {
            UiTooltipTrigger.Wire(trigger, ContentId, controls, Invoker(clicks));
        }
        else
        {
            root = Spoken(root, toggled);
        }

        var scope = new UiTooltipScope(
            ContentId,
            Position ?? Ui.TooltipPosition.Top,
            Align ?? Ui.TooltipAlign.Center,
            Gap,
            Offset,
            Toggled: clicks,
            Described: !controls);

        return root[
            Context.Provide(scope)[
                parts,
                Content is { } text ? Ui.TooltipContent.Kbd(Kbd)[text] : null
            ]
        ];
    }

    // The wrapper's markers: Flux's, and the two states the stylesheet reads.
    private Dictionary<string, string?> Marks(bool toggled)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-tooltip"] = null };
        if (toggled)
        {
            marks["toggleable"] = null;
        }

        if (Disabled == true)
        {
            marks["disabled"] = null;
        }

        return marks;
    }

    // What opens it from the trigger with no script, where the trigger can be one.
    private string? Invoker(bool clicks)
    {
        if (Disabled == true)
        {
            return null;
        }

        return clicks ? "popovertarget" : "interestfor";
    }

    // The trigger is not an element the kit can reach into — or not one a click can open a popover from — so
    // the wrapper says it, as a group with a description, and takes the focus a tap gives when that is what
    // has to show it.
    private HTMLDivElement Spoken(HTMLDivElement root, bool focusable)
    {
        var spoken = root.Role("group").Aria("describedby", ContentId);
        return focusable ? spoken.TabIndex(0) : spoken;
    }
}
