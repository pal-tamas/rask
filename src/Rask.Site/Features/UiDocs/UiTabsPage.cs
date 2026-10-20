using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's tabs and their parts as a C# component: each example with its own code, and its reference.</summary>
[Route("tabs")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTabsPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Tabs.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Tabs)
            .Lead("A row of tabs, a segmented control or pills, over panels or on their own. Switching is drawn in the "
                  + "browser, and the selected tab is a value like any other control's: bound, handed to a handler, or "
                  + "left to the row.")
            .Api(UiApi.Tabs)[
            UiDocsExample.Key("ui-tabs").Heading("Tabs").File("TabsExample.cs").Demo(TabsExample).Testid("ui-tabs")
                .Blurb("Over panels, with the selected tab's name held by the page: Value, and OnChange to hear it."),
            UiDocsExample.Key("ui-tabs-findable").Heading("Findable tabs").File("TabsFindableExample.cs").Demo(TabsFindableExample).Testid("ui-tabs-findable")
                .Blurb("Search the page for \"INV-1042\": the browser finds it on a hidden panel and selects its tab."),
            UiDocsExample.Key("ui-tabs-icons").Heading("With icons").File("TabsIconsExample.cs").Demo(TabsIconsExample).Testid("ui-tabs-icons"),
            UiDocsExample.Key("ui-tabs-padded").Heading("Padded edges").File("TabsPaddedExample.cs").Demo(TabsPaddedExample).Testid("ui-tabs-padded")
                .Blurb("A class of yours moves the first tab in from the hairline's start."),
            UiDocsExample.Key("ui-tabs-scrollable").Heading("Scrollable tabs").File("TabsScrollableExample.cs").Demo(TabsScrollableExample).Testid("ui-tabs-scrollable")
                .Blurb("More tabs than room: the row scrolls sideways instead of pushing the page wider than a phone."),
            UiDocsExample.Key("ui-tabs-fade").Heading("Scrollable with a fade").File("TabsScrollableFadeExample.cs").Demo(TabsScrollableFadeExample).Testid("ui-tabs-fade")
                .Blurb("ScrollableFade() fades the trailing edge while there are tabs past it."),
            UiDocsExample.Key("ui-tabs-segmented").Heading("Segmented tabs").File("TabsSegmentedExample.cs").Demo(TabsSegmentedExample).Testid("ui-tabs-segmented")
                .Blurb("A choice on its own, bound to a property. The page has the last word: the button sets the "
                       + "property, and the row shows it."),
            UiDocsExample.Key("ui-tabs-segmented-icons").Heading("Segmented with icons").File("TabsSegmentedIconsExample.cs").Demo(TabsSegmentedIconsExample).Testid("ui-tabs-segmented-icons"),
            UiDocsExample.Key("ui-tabs-segmented-sm").Heading("Small segmented tabs").File("TabsSegmentedSmallExample.cs").Demo(TabsSegmentedSmallExample).Testid("ui-tabs-segmented-sm"),
            UiDocsExample.Key("ui-tabs-pills").Heading("Pill tabs").File("TabsPillsExample.cs").Demo(TabsPillsExample).Testid("ui-tabs-pills"),
            UiDocsExample.Key("ui-tabs-dynamic").Heading("Dynamic tabs").File("TabsDynamicExample.cs").Demo(TabsDynamicExample).Testid("ui-tabs-dynamic")
                .Blurb("An Action() tab is a button in the row: it adds a tab and selects nothing."),
            UiDocsExample.Key("ui-tabs-disabled").Heading("A disabled tab, and counts").File("TabsDisabledExample.cs").Demo(TabsDisabledExample).Testid("ui-tabs-disabled")
                .Blurb("A count is a child like any other; a disabled tab is passed over by the pointer and the arrows."),
            UiTabsBehaviour
        ];
}
