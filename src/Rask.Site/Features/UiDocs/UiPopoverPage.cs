using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's popover as a C# component: each example with its own code, and its reference.</summary>
[Route("popover")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiPopoverPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Popover.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Popover)
            .Lead("Extra content in a panel beside a button, for what is not a menu. Ui.Popover is the second "
                  + "child of the Ui.Dropdown that holds its trigger, and where it opens is the dropdown's. The "
                  + "browser opens it and closes it on Escape and on a press anywhere else.")
            .Api(UiApi.Popover)[
            UiDocsExample.Key("ui-popover-intro").Heading("Popover").File("PopoverExample.cs").Demo(PopoverExample).Testid("ui-popover-intro")
                .Blurb("Focus stays on the trigger as it opens, and Tab walks from there into the panel. While it is "
                       + "up the page behind neither scrolls nor takes the pointer."),
            UiDocsExample.Key("ui-popover-hover").Heading("Hover trigger").File("PopoverHoverExample.cs").Demo(PopoverHoverExample).Testid("ui-popover-hover")
                .Blurb("Hover() on the dropdown: the pointer opens it and leaving closes it. A press and Enter open "
                       + "it too, and it locks nothing."),
            UiDocsExample.Key("ui-popover-position").Heading("Position").File("PopoverPositionExample.cs").Demo(PopoverPositionExample).Testid("ui-popover-position")
                .Blurb("The side the panel opens on, and where along it."),
            UiDocsExample.Key("ui-popover-gap").Heading("Gap & offset").File("PopoverGapExample.cs").Demo(PopoverGapExample).Testid("ui-popover-gap")
                .Blurb("Gap is the distance from the trigger; Offset shifts the panel along its edge."),
            UiDocsSection.Key("examples").Heading("Examples")[
                UiDocsExample.Key("ui-popover-categories").Heading("Category picker").File("PopoverCategoriesExample.cs").Demo(PopoverCategoriesExample).Testid("ui-popover-categories").Nested()
                    .Blurb("A list of selectable categories, bound to the page. Picking keeps the panel open."),
                UiDocsExample.Key("ui-popover-feedback").Heading("Feedback").File("PopoverFeedbackExample.cs").Demo(PopoverFeedbackExample).Testid("ui-popover-feedback").Nested()
                    .Blurb("A form in a panel. The page owns Open, so the form's own buttons close it, and OnToggle "
                           + "is how the page hears Escape and a press outside."),
                UiDocsExample.Key("ui-popover-event").Heading("Event details").File("PopoverEventExample.cs").Demo(PopoverEventExample).Testid("ui-popover-event").Nested()
                    .Blurb("Detailed information about an event, in a panel with no padding of its own.")
            ]
        ];
}
