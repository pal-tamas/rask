using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>Ui.FormTabs</c>' row held to Flux's: the kit's own part (owner-approved, not Flux's) is drawn of Flux's tabs, so
///     with no message on its form it is Flux's scrollable tab group to the pixel. Measured against
///     fluxui.dev/components/tabs by <c>flux parity form-tabs --prepare form-tabs</c>, on the one example a long form's
///     row is — <c>scrollable-tabs</c>, seven tabs in the docs page's 384px column. The red count on a tab is
///     <see cref="UiBadge" />, which the <c>badge</c> page holds.
/// </summary>
public sealed partial class FormTabsParity : FluxParity
{
    // `<flux:tab.panel name="…">...</flux:tab.panel>` as the demos render it, with no padding: an app's utility,
    // under a name of this page's own.
    private const string AppUtilities = "<style>.parity-pt-0{padding-top:0}</style>";

    private static readonly string[] Seven =
        ["Profile", "Account", "Billing", "Security", "Notifications", "Integrations", "API"];

    public override string Page => "form-tabs";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("scrollable-tabs", Div.Style("max-width:384px;margin:0 auto")[
            Raw.Value(AppUtilities),
            Ui.FormTabs[Seven.Select(label => Ui.FormTab.Key(label).Name(label.ToLowerInvariant()).Label(label).Class("parity-pt-0"))]
        ]);
    }
}
