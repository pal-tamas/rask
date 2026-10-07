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
///     The triggers are the kit's own <c>Ui.Button</c> and the second example sits in a <c>Ui.Heading</c>,
///     as on Flux's page.
///     </para>
/// </remarks>
public sealed partial class TooltipParity : FluxParity
{
    /// <summary>The utilities Flux's examples hand to <c>class</c>: an app's own, so stated here under names of the page's.</summary>
    internal const string AppClasses =
        "<style>.parity-heading{display:flex;align-items:center;gap:.5rem}"
        + ".parity-info{max-width:20rem}.parity-info>:not(:last-child){margin-bottom:.5rem}</style>";

    public override string Page => "tooltip";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Alone in a block on Flux's page, where it is an inline box.
        yield return ("", Div[Raw.Value(AppClasses), Ui.Tooltip.Content("Settings")[Settings()]]);

        yield return ("info-tooltip", Row(Div[
            Ui.Heading.Class("parity-heading")[
                "Tax identification number",
                Ui.Tooltip.Toggleable()[
                    Info(),
                    Ui.TooltipContent.Class("parity-info")[
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

    /// <summary><c>&lt;flux:button icon="cog-6-tooth" icon:variant="outline" /&gt;</c>.</summary>
    internal static Component Settings() => Ui.Button.Icon(Ui.IconName.Cog6Tooth).IconVariant(Ui.IconVariant.Outline);

    /// <summary><c>&lt;flux:button icon="information-circle" size="sm" variant="ghost" /&gt;</c>.</summary>
    internal static Component Info() => Ui.Button.Icon(Ui.IconName.InformationCircle).Sm.Ghost;
}
