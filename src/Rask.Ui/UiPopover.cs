using System.Globalization;
using Rask.Core.Live;

namespace Rask;

/// <summary>
/// A button that opens a panel of anything beside it.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's popover, and the gap <see cref="UiDropdown" /> left: a dropdown is a MENU — its children are rows
/// you pick from, and it says <c>role="menu"</c> and moves a keyboard cursor over them. A filter panel, a
/// colour picker or a bubble of help is none of those, and putting it in a menu tells a screen reader it is a
/// list of commands and traps the arrow keys inside it.
/// </para>
/// <para>
/// So this is the same machinery with no menu semantics: a <c>[popover]</c>, which the browser lifts into the
/// top layer, dismisses on Escape and on a click outside, and hands focus back from — placed by CSS anchor
/// positioning, with the same <see cref="Position" /> and <see cref="Align" /> vocabulary everything else that
/// floats uses. Inside it, Tab moves through whatever you put there, because it is ordinary content.
/// </para>
/// <para>
/// It needs no runtime to open and close. <see cref="Open" /> takes ownership when a page has to open it
/// itself, and the runtime mirrors that through <c>data-rask-popover-open</c>.
/// </para>
/// </remarks>
public sealed partial class UiPopover : Component
{
    // Per instance, so two popovers on one page cannot collide on the ids that join a trigger to its panel.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private bool _open;

    /// <summary>The label on the button that opens it.</summary>
    public required string Trigger { get; set; }

    /// <summary>Which side of the trigger the panel opens on. Below, unless this says otherwise.</summary>
    public Ui.Position? Position { get; set; }

    /// <summary>Where along that side the panel sits.</summary>
    public Ui.Align? Align { get; set; }

    /// <summary>The distance between the trigger and the panel, in pixels. Defaults to 4.</summary>
    public int? Gap { get; set; }

    /// <summary>An icon before the trigger's label.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon after the trigger's label. A chevron unless this says otherwise.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>How the trigger is drawn — the same axes every button takes.</summary>
    public Ui.Tone? Tone { get; set; }

    /// <inheritdoc cref="UiButton.Variant" />
    public Ui.Variant? Variant { get; set; }

    /// <inheritdoc cref="UiButton.Size" />
    public Ui.Size? Size { get; set; }

    /// <summary>
    ///     Whether the panel is open. Unset, the reader opens and closes it and the page is not asked.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the reader opens or closes it, with the state it is now in.</summary>
    public Callback<bool> OnToggle { get; set; }

    /// <summary>Classes for the panel itself — its width, its padding.</summary>
    public string? PanelClass { get; set; }

    public string? Class { get; set; }

    // _open is a FIELD, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string Prefix => "uipop-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string TriggerId => Prefix + "-trigger";

    private string PanelId => Prefix + "-panel";

    /// <inheritdoc />
    protected override Component? Render()
    {
        var open = Open ?? _open;

        var trigger = Button
            .Id(TriggerId)
            .Type("button")
            .Class(UiClass.Compose(
                "btn",
                Tone is { } tone ? UiClassNames.ButtonTone(tone) : "",
                Variant is { } variant ? UiClassNames.ButtonVariant(variant) : "",
                Size is { } size ? UiClassNames.ButtonSize(size) : ""))
            // aria-haspopup="dialog" rather than "menu": what opens is a panel of content, and saying menu
            // would promise a list of commands and the arrow keys that walk it.
            .Aria(new Dictionary<string, string?>
            {
                ["haspopup"] = "dialog",
                ["expanded"] = open ? "true" : "false",
                ["controls"] = PanelId,
            })
            .Attributes(("popovertarget", PanelId), ("style", "anchor-name:--" + Prefix));

        var panel = Div
            .Id(PanelId)
            .Popover("auto")
            .Role("dialog")
            .Class(UiClass.Compose(
                "z-1 rounded-box border border-base-300 bg-base-100 p-4 shadow-sm",
                PanelClass))
            .Attributes(("style", PanelStyle()))
            // The SOLE writer of the open state: the browser opens the popover from `popovertarget` and closes
            // it on Escape and on a click outside, and this is where C# hears which.
            .Aria("labelledby", TriggerId)
            .OnToggle(OnPanelToggleAsync);

        if (Open is { } controlled)
        {
            // The runtime shows or hides the popover to match whenever this changes (rask-dom.ts).
            panel = panel.Data("rask-popover-open", controlled ? "true" : "false");
        }

        var root = Div.Class(UiClass.Compose("relative inline-block", Class));
        if (open)
        {
            root = root.Data("open", "");
        }

        return root[
            trigger[
                Icon is { } icon ? Ui.Icon.Name(icon).Class("size-4 shrink-0") : null,
                Span[Trigger],
                Ui.Icon.Name(IconTrailing ?? Ui.IconName.ChevronDown).Class("size-4 shrink-0 opacity-60")
            ],
            panel[Children ?? []]
        ];
    }

    // The same anchor positioning Ui.Dropdown and Ui.Select use. An engine without it keeps the popover's own
    // default, centred — still open and still usable.
    private string PanelStyle()
    {
        var side = (Position ?? Ui.Position.Bottom) switch
        {
            Ui.Position.Top => "block-start",
            Ui.Position.Left => "inline-start",
            Ui.Position.Right => "inline-end",
            _ => "block-end",
        };
        var vertical = (Position ?? Ui.Position.Bottom) is Ui.Position.Left or Ui.Position.Right;
        var along = Align switch
        {
            Ui.Align.Center => "center",
            Ui.Align.End => vertical ? "span-block-start" : "span-inline-start",
            _ => vertical ? "span-block-end" : "span-inline-end",
        };

        return "position-anchor:--" + Prefix
               + ";position-area:" + side + " " + along
               + ";position-try-fallbacks:flip-block,flip-inline"
               + ";margin:" + (Gap ?? 4).ToString(CultureInfo.InvariantCulture) + "px";
    }

    private async Task OnPanelToggleAsync(ToggleEventArgs e)
    {
        _open = e.IsOpen;

        // Controlled: tell the page only about a change it did not make itself — the runtime showing the
        // popover because Open became true fires this same event.
        if (e.IsOpen != Open)
        {
            await OnToggle.Invoke(e.IsOpen).ConfigureAwait(false);
        }
    }
}
