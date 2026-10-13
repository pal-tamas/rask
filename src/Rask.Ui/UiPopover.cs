using Rask.Core.Live;

namespace Rask;

/// <summary>
/// Extra content in a panel that opens beside a button, on a press or on hover. Flux UI's <c>flux:popover</c>.
/// </summary>
/// <remarks>
/// <para>
/// For content that is not a menu: a form, a set of filters, a card about a person. It is the second child of
/// the <see cref="UiDropdown" /> that holds its trigger, and that is where its side, its alignment, its gap and
/// what opens it are said.
/// </para>
/// <code>
/// Ui.Dropdown[
///     Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Options"],
///     Ui.Popover.Class("flex flex-col gap-4")[
///         Ui.RadioGroup.Bind(() => _sort).Label("Sort by")[
///             Ui.Radio.Value("active").Label("Recently active"),
///             Ui.Radio.Value("posted").Label("Date posted")
///         ]
///     ]
/// ]
/// </code>
/// <para>
/// It is a <c>[popover]</c> the trigger names with <c>popovertarget</c>: the browser lifts it into the top
/// layer and closes it on Escape and on a press anywhere else, and focus stays on the trigger, from where Tab
/// walks into the panel. While one is open the page behind neither scrolls nor takes the pointer — except
/// behind one the pointer opened (<see cref="UiDropdown.Hover" />), which locks nothing.
/// </para>
/// </remarks>
public sealed partial class UiPopover : Component
{
    // Flux's panel, measured: 12rem at least, 1rem of padding, a hairline border on a white (zinc-700) card.
    // Each default weighs nothing, so a width, a padding, a radius or a shadow from the call site wins.
    // Closed it is not displayed whatever layout the call site gives it: a `flex` would show a closed panel.
    private const string Panel =
        "[:where(&)]:min-w-48 [:where(&)]:p-4 [:where(&)]:rounded-lg [:where(&)]:shadow-xs border border-zinc-200 "
        + "bg-white [:where(&)]:text-black [&:not(:popover-open)]:hidden dark:border-zinc-600 dark:bg-zinc-700 "
        + "dark:[:where(&)]:text-white";

    private UiPopupHost? _host;

    /// <summary>Classes for the panel — its width, its layout, a larger radius or shadow.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        _host = Context.Get<UiPopupHost>();

        // Flux's mark on the open panel, which its own examples style by (`data-open:flex`), is the runtime's
        // to write (rask-toggle.ts): there in the task that opens the panel, where a render would bring it a
        // round trip later.
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-popover"] = "", ["rask-open-mark"] = "" };

        if (_host?.Place is { } place)
        {
            data["rask-place"] = place;
        }

        if (_host?.Hover != true)
        {
            // The page behind neither scrolls nor takes the pointer, as under Flux's (rask-lock.ts). Not under
            // one the pointer opens: a page with no pointer would take it from the trigger too.
            data["rask-lock"] = "";
        }

        if (_host?.Controlled is { } controlled)
        {
            // The runtime shows or hides the popover to match whenever this changes (rask-dom.ts).
            data["rask-popover-open"] = controlled ? "true" : "false";
        }

        if (_host?.ToggleDraws != false)
        {
            // Open or shut, nobody of the page's is told: the toggle is heard only to draw (UiDraws).
            data["rask-draws"] = "toggle";
        }

        // No `autofocus`: Flux leaves focus on the trigger, and Tab goes from there into the panel after it.
        var panel = Div
            .Id(_host?.PanelId)
            .Popover(Popover.Auto)
            .TabIndex(-1)
            .Class(UiClass.Compose(Panel, Class))
            .Data(data)
            // The SOLE writer of the open state: the browser opens the popover and closes it on Escape, a press
            // outside and Tab, and this is where C# hears which.
            .OnToggle(OnToggleAsync);

        return (_host is { } host ? panel.Style(host.Style) : panel)[Children ?? []];
    }

    private async Task OnToggleAsync(ToggleEvent e)
    {
        if (_host is { } host)
        {
            await host.Toggled(string.Equals(e.NewState, "open", StringComparison.Ordinal)).ConfigureAwait(false);
        }
    }
}
