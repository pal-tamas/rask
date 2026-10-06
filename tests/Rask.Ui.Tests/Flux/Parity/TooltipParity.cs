using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/tooltip</c>, example for example, as the page loads: every tooltip closed.
/// </summary>
/// <remarks>
///     <para>
///     A closed tooltip is most of nothing, so the SHOWN one is proved separately:
///     <c>node scripts/flux/parity-tooltip.mjs</c> shows each tooltip on Flux's live page and on
///     <see cref="TooltipShownParity" />'s, and compares the box, its colours, its gap and its alignment.
///     </para>
///     <para>
///     The triggers are Flux buttons and the second example sits in a Flux heading. Neither is this
///     component, so each is a stand-in drawn from Flux's measurements (<see cref="StandIns" />) until its own
///     component lands; the tooltip around it is the real one.
///     </para>
/// </remarks>
public sealed partial class TooltipParity : FluxParity
{
    /// <summary>
    ///     Flux's outline and ghost icon buttons and its heading, as measured, plus the two utilities the
    ///     info example hands to the content — an app's own Tailwind build would emit those.
    /// </summary>
    internal const string StandIns =
        "<style>"
        + ".fx-button{position:relative;display:inline-flex;align-items:center;justify-content:center;gap:8px;"
        + "width:40px;height:40px;font-size:14px;line-height:20px;font-weight:500;white-space:nowrap;cursor:default;"
        + "color:oklch(.274 .006 286.033);background:#fff;border:1px solid oklch(.92 .004 286.32);"
        + "border-bottom-color:color-mix(in oklab,oklch(.871 .006 286.286) 80%,transparent);border-radius:8px;"
        + "box-shadow:0 0 #0000,0 0 #0000,0 0 #0000,0 0 #0000,0 1px 2px 0 rgba(0,0,0,.05)}"
        + ".fx-button:hover{background:oklch(.985 0 0);border-bottom-color:oklch(.92 .004 286.32)}"
        + ".fx-button svg{width:20px;height:20px}"
        + ".dark .fx-button{color:#fff;background:oklch(.37 .013 285.805);border-color:oklch(.442 .017 285.786)}"
        + ".dark .fx-button:hover{background:color-mix(in oklab,oklch(.442 .017 285.786) 75%,transparent)}"
        + ".fx-ghost{position:relative;display:inline-flex;align-items:center;justify-content:center;gap:8px;"
        + "width:32px;height:32px;font-size:14px;line-height:20px;font-weight:500;white-space:nowrap;cursor:default;"
        + "color:oklch(.274 .006 286.033);border-radius:6px}"
        + ".fx-ghost:hover{background:color-mix(in oklab,oklch(.274 .006 286.033) 5%,transparent)}"
        + ".dark .fx-ghost{color:#fff}"
        + ".dark .fx-ghost:hover{background:color-mix(in oklab,#fff 15%,transparent)}"
        + ".fx-heading{display:flex;align-items:center;gap:8px;font-size:14px;line-height:20px;font-weight:500;"
        + "color:oklch(.274 .006 286.033)}.dark .fx-heading{color:#fff}"
        + ".max-w-\\[20rem\\]{max-width:20rem}.space-y-2>:not(:last-child){margin-bottom:.5rem}"
        + "</style>";

    public override string Page => "tooltip";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Alone in a block on Flux's page, where it is an inline box.
        yield return ("", Div[Raw.Value(StandIns), Ui.Tooltip.Content("Settings")[Settings()]]);

        yield return ("info-tooltip", Row(Div[
            Div.Class("fx-heading").Data("ui-heading", null)[
                "Tax identification number",
                Ui.Tooltip.Toggleable()[
                    Button.Type(ButtonType.Button).Class("fx-ghost")[Ui.Icon.Name(Ui.IconName.InformationCircle).Mini],
                    Ui.TooltipContent.Class("max-w-[20rem] space-y-2")[
                        P["For US businesses, enter your 9-digit Employer Identification Number (EIN) without hyphens."],
                        P["For European companies, enter your VAT number including the country prefix (e.g., DE123456789)."]
                    ]
                ]
            ]
        ]));

        yield return ("position", Div.Style("display:grid;grid-template-columns:repeat(4,max-content);gap:64px;justify-content:center")[
            Ui.Tooltip.Content("Settings").Top[Settings()],
            Ui.Tooltip.Content("Settings").Right[Settings()],
            Ui.Tooltip.Content("Settings").Bottom[Settings()],
            Ui.Tooltip.Content("Settings").Left[Settings()]
        ]);
    }

    /// <summary><c>&lt;flux:button icon="cog-6-tooth" icon:variant="outline" /&gt;</c>, as measured.</summary>
    internal static Component Settings() =>
        Button.Type(ButtonType.Button).Class("fx-button")[Ui.Icon.Name(Ui.IconName.Cog6Tooth)];
}
