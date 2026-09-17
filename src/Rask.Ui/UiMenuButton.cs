using System.Globalization;

namespace Rask.Ui;

/// <summary>
/// A control that opens a menu beside it: the trigger, and where the menu sits against it.
/// </summary>
/// <remarks>
/// What <see cref="UiDropdown" /> and <see cref="UiProfile" /> share, which is everything except what the button
/// LOOKS like. The menu itself — the popover, and the keyboard cursor inside it — is
/// <see cref="UiMenuSurface" />'s; this adds the button that opens it, which is where focus goes back to when it
/// closes.
/// </remarks>
public abstract partial class UiMenuButton : UiMenuSurface
{
    /// <summary>Which side of the trigger the menu opens on. Below, unless this says otherwise.</summary>
    public UiPosition? Position { get; set; }

    /// <summary>Where along that side the menu sits — flush with the trigger's start, centred, or its end.</summary>
    public UiAlign? Align { get; set; }

    /// <summary>The distance between the trigger and the menu, in pixels. Defaults to 4.</summary>
    public int? Gap { get; set; }

    /// <summary>Shifts the menu along its alignment, in pixels.</summary>
    public int? Offset { get; set; }

    /// <summary>The classes on the button itself.</summary>
    private protected abstract string TriggerClass { get; }

    /// <summary>The classes on the wrapper the popover is anchored inside.</summary>
    private protected virtual string RootClass => "relative inline-block";

    /// <summary>Which way the menu opens when the call site did not say.</summary>
    private protected virtual UiPosition DefaultPosition => UiPosition.Bottom;

    private protected string TriggerId => Prefix + "-trigger";

    /// <summary>What goes inside the button — the label and its icons, or a whole profile row.</summary>
    private protected abstract Component TriggerContent();

    /// <summary>The trigger, the popover and the menu: the shape every menu button in the kit renders.</summary>
    private protected Component MenuButton()
    {
        var open = IsOpen;

        var trigger = Button
            .Id(TriggerId)
            .Type("button")
            .Class(TriggerClass)
            .Aria(new Dictionary<string, string?>
            {
                ["haspopup"] = "menu",
                ["expanded"] = open ? "true" : "false",
                ["controls"] = MenuId,
            })
            .Attributes(("popovertarget", PanelId), ("style", "anchor-name:--" + Prefix));

        var root = Div.Class(UiClass.Compose(RootClass, Class));
        if (open)
        {
            // A styling hook, Flux's `data-open`: a trigger that looks pressed while its menu is up.
            root = root.Data("open", "");
        }

        return root[
            trigger[TriggerContent()],
            MenuPanel(PanelStyle(), TriggerId)
        ];
    }

    // Placement by CSS anchor positioning, in a style attribute: nothing here is scanned by Tailwind, so the
    // values are built freely. An engine without anchor positioning keeps the popover's own default, centred —
    // still open and usable, the trade UiSelect and UiMegamenu already make.
    private string PanelStyle()
    {
        var side = (Position ?? DefaultPosition) switch
        {
            UiPosition.Top => "block-start",
            UiPosition.Left => "inline-start",
            UiPosition.Right => "inline-end",
            _ => "block-end",
        };
        var vertical = (Position ?? DefaultPosition) is UiPosition.Left or UiPosition.Right;
        var along = Align switch
        {
            UiAlign.Center => "center",
            UiAlign.End => vertical ? "span-block-start" : "span-inline-start",
            _ => vertical ? "span-block-end" : "span-inline-end",
        };
        var gap = (Gap ?? 4).ToString(CultureInfo.InvariantCulture);
        var offset = (Offset ?? 0).ToString(CultureInfo.InvariantCulture);

        return "position-anchor:--" + Prefix
               + ";position-area:" + side + " " + along
               + ";position-try-fallbacks:flip-block,flip-inline"
               + ";margin:" + gap + "px"
               + ";translate:" + (vertical ? "0 " + offset + "px" : offset + "px 0")
               // Submenus fly out beside their row, so the panel must not clip what overflows it.
               + ";overflow:visible";
    }
}
