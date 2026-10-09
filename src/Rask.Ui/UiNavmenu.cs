using System.Globalization;
using Rask.Core.Live;

namespace Rask;

/// <summary>
/// A short list of links in a dropdown. Flux UI's <c>flux:navmenu</c>.
/// </summary>
/// <remarks>
/// <para>
/// What a <see cref="UiDropdown" /> opens when the rows are places rather than actions: a <c>&lt;nav&gt;</c> of
/// ordinary links, drawn as a <see cref="UiMenu" /> is. It has no menu roles and no keyboard cursor — Tab walks
/// the links, as it walks any others — so reach for <see cref="UiMenu" /> when the rows are commands, or need
/// submenus, checkboxes or radios.
/// </para>
/// <code>
/// Ui.Dropdown.End[
///     Ui.Button["Olivia Martin"],
///     Ui.Navmenu[
///         Ui.NavmenuItem.Href(Routes.AccountPage()).Icon(Ui.IconName.User)["Account"],
///         Ui.NavmenuItem.Href(Routes.BillingPage()).Icon(Ui.IconName.CreditCard)["Billing"]
///     ]
/// ]
/// </code>
/// </remarks>
public sealed partial class UiNavmenu : Component
{
    // Per instance: a navmenu outside a dropdown still needs an id of its own.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private UiPopupHost? _host;

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        _host = Context.Get<UiPopupHost>();
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-navmenu"] = "" };
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

        var nav = Nav
            .Id(_host?.PanelId ?? "uinavmenu-" + _instance.ToString(CultureInfo.InvariantCulture))
            .Popover(Popover.Auto)
            .Class(UiClass.Compose(UiMenuRow.Box, Class))
            .Data(data)
            .OnToggle(OnToggleAsync);

        return (_host is { } host ? nav.Style(host.Style) : nav)[Children ?? []];
    }

    private Task OnToggleAsync(ToggleEvent e) =>
        _host is { } host
            ? host.Toggled(string.Equals(e.NewState, "open", StringComparison.Ordinal))
            : Task.CompletedTask;
}
