using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     The page <c>scripts/flux/parity-tooltip.mjs</c> shows every tooltip on, case by case.
/// </summary>
/// <remarks>
///     <para>
///     A tooltip is the thing that appears, and <see cref="TooltipParity" /> only ever measures it closed.
///     That script hovers (or clicks) each case here and its twin on Flux's live page, and compares the box
///     that appears: its size, where it sits against its trigger, its colours and its type.
///     </para>
///     <para>
///     Cases are paired by <c>data-case</c>. The first seven are on Flux's page as written; the rest are
///     props its page documents and does not show (<c>align</c>, <c>gap</c>, <c>offset</c>,
///     <c>interactive</c>, <c>disabled</c>, a trigger that is not a button), which the script builds there
///     from Flux's own element, around the same trigger this page uses.
///     </para>
/// </remarks>
public sealed partial class TooltipShownParity : FluxParity
{
    private const string Save = "width:120px;height:40px;border:1px solid gray";

    private const string Wide = "A fairly wide tooltip text here";

    public override string Page => "tooltip-shown";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("page", Grid(
            Raw.Value(TooltipParity.AppClasses),
            Case("intro", Ui.Tooltip.Content("Settings")[TooltipParity.Settings()]),
            Case("position top", Ui.Tooltip.Content("Settings").Top[TooltipParity.Settings()]),
            Case("position right", Ui.Tooltip.Content("Settings").Right[TooltipParity.Settings()]),
            Case("position bottom", Ui.Tooltip.Content("Settings").Bottom[TooltipParity.Settings()]),
            Case("position left", Ui.Tooltip.Content("Settings").Left[TooltipParity.Settings()]),
            // The docs header's own button: `tooltip="Toggle dark mode" tooltip:position="bottom" tooltip:kbd="D"`.
            Case("kbd", Ui.Tooltip.Content("Toggle dark mode").Kbd("D").Bottom[TooltipParity.Settings()]),
            Case("info", Ui.Tooltip.Toggleable()[
                TooltipParity.Info(),
                Ui.TooltipContent.Class("parity-info")[
                    P["For US businesses, enter your 9-digit Employer Identification Number (EIN) without hyphens."],
                    P["For European companies, enter your VAT number including the country prefix (e.g., DE123456789)."]
                ]
            ])));

        yield return ("align", Grid([.. Aligned()]));

        yield return ("props", Grid(
            Case("gap 12", Ui.Tooltip.Content("Settings").Gap(12)[SaveButton()]),
            Case("offset 20", Ui.Tooltip.Content("Settings").Offset(20)[SaveButton()]),
            Case("offset 20 start", Ui.Tooltip.Content("Settings").Start.Offset(20)[SaveButton()]),
            Case("offset 8 end", Ui.Tooltip.Content("Settings").End.Offset(8)[SaveButton()]),
            Case("right gap 12 offset 20", Ui.Tooltip.Content("Settings").Right.Gap(12).Offset(20)[SaveButton()]),
            Case("left start offset 8", Ui.Tooltip.Content("Settings").Left.Start.Offset(8)[SaveButton()]),
            Case("interactive", Ui.Tooltip.Content("Settings").Interactive()[SaveButton()]),
            Case("disabled", Ui.Tooltip.Content("Settings").Disabled()[SaveButton()]),
            // Not a button, so not an interest invoker: the stylesheet's :hover shows this one.
            Case("span", Ui.Tooltip.Content("Settings")[Span.TabIndex(0)["plain text"]]),
            Case("disabled button", Ui.Tooltip.Content("Settings")[Button.Type(ButtonType.Button).Disabled(true).Style(Save)["Save file"]])));

        // Moved against an edge of the viewport by the script, where each has to flip or slide.
        yield return ("edges", Grid(
            Case("wide top", Ui.Tooltip.Content(Wide).Top[Small()]),
            Case("wide bottom", Ui.Tooltip.Content(Wide).Bottom[Small()]),
            Case("wide left", Ui.Tooltip.Content(Wide).Left[Small()]),
            Case("wide right", Ui.Tooltip.Content(Wide).Right[Small()])));
    }

    private static IEnumerable<Component> Aligned()
    {
        foreach (var position in Enum.GetValues<Ui.TooltipPosition>())
        {
            foreach (var align in Enum.GetValues<Ui.TooltipAlign>())
            {
                yield return Case(
                    $"{position} {align}".ToLowerInvariant(),
                    Ui.Tooltip.Content("Settings").Position(position).Align(align)[SaveButton()]);
            }
        }
    }

    private static Component SaveButton() => Button.Type(ButtonType.Button).Style(Save)["Save file"];

    private static Component Small() => Button.Type(ButtonType.Button).Style("width:40px;height:40px;border:1px solid gray")["o"];

    private static Component Case(string name, Component tooltip) => Div.Data("case", name)[tooltip];

    // Room on every side of every case, so no tooltip has to flip.
    private static Component Grid(params Component[] cases) =>
        Div.Style("display:flex;flex-wrap:wrap;gap:100px 150px;justify-content:center;padding:60px 120px")[cases];
}
