using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's tooltip as a C# component: each example with its own code, and its reference.</summary>
[Route("tooltip")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTooltipPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Tooltip.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Tooltip)
            .Lead("A line of help beside whatever it wraps, shown on hover and on keyboard focus, and said by a screen "
                  + "reader because the trigger is wired to it. It is a hint: nothing that matters should live only "
                  + "here, and what must reach a phone is Toggleable, opened by a tap.")
            .Api(UiApi.Tooltip)[
            UiDocsExample.Key("ui-tooltip").Heading("Tooltip").File("TooltipExample.cs").Demo(TooltipExample).Testid("ui-tooltip")
                .Blurb("The first child is the trigger. An icon button has no text of its own, so the tooltip names it."),
            UiDocsExample.Key("ui-tooltip-info").Heading("Info tooltip").File("TooltipInfoExample.cs").Demo(TooltipInfoExample).Testid("ui-tooltip-info")
                .Blurb("More than a line, and essential: Toggleable opens it on a click or a tap, and a Ui.TooltipContent "
                       + "holds what it says."),
            UiDocsExample.Key("ui-tooltip-positions").Heading("Position").File("TooltipPositionExample.cs").Demo(TooltipPositionExample).Testid("ui-tooltip-positions")
                .Blurb("Above unless it says otherwise: right, below or left."),
            UiDocsExample.Key("ui-tooltip-align").Heading("Align").File("TooltipAlignExample.cs").Demo(TooltipAlignExample).Testid("ui-tooltip-align")
                .Blurb("Centred on its side unless Start or End puts it flush with the trigger's edge."),
            UiDocsExample.Key("ui-tooltip-kbd").Heading("Shortcut").File("TooltipKbdExample.cs").Demo(TooltipKbdExample).Testid("ui-tooltip-kbd")
                .Blurb("Kbd shows a shortcut after the text."),
            UiDocsExample.Key("ui-tooltip-disabled").Heading("Disabled button").File("TooltipDisabledExample.cs").Demo(TooltipDisabledExample).Testid("ui-tooltip-disabled")
                .Blurb("A disabled button takes no pointer, so the hover lands on the wrapper and the tooltip still says why."),
            UiTooltipBehaviour
        ];
}
