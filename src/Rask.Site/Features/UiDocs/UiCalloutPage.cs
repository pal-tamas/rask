using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's callout as a C# component: each example with its own code, and its reference.</summary>
[Route("callout")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiCalloutPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Callout.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Callout)
            .Lead("A message set apart from the page: an icon, a heading, text, and what to do about it. It "
                  + "announces nothing by itself; one that appears because something happened says so with Role.")
            .Api(UiApi.Callout)[
            UiDocsExample.Key("ui-callout-intro").Heading("Callout").File("CalloutExample.cs").Demo(CalloutExample).Testid("ui-callout-intro"),
            UiDocsExample.Key("ui-callout-heading-icon").Heading("Icon inside heading").File("CalloutHeadingIconExample.cs").Demo(CalloutHeadingIconExample).Testid("ui-callout-heading-icon"),
            UiDocsExample.Key("ui-callout-custom-icon").Heading("Custom icon").File("CalloutCustomIconExample.cs").Demo(CalloutCustomIconExample).Testid("ui-callout-custom-icon")
                .Blurb("Any SVG of your own in place of a Heroicon."),
            UiDocsExample.Key("ui-callout-actions").Heading("With actions").File("CalloutActionsExample.cs").Demo(CalloutActionsExample).Testid("ui-callout-actions"),
            UiDocsExample.Key("ui-callout-inline-actions").Heading("Inline actions").File("CalloutInlineActionsExample.cs").Demo(CalloutInlineActionsExample).Testid("ui-callout-inline-actions")
                .Blurb("Inline puts the actions beside the text, where there is room."),
            UiDocsExample.Key("ui-callout-dismissible").Heading("Dismissible").File("CalloutDismissibleExample.cs").Demo(CalloutDismissibleExample).Testid("ui-callout-dismissible")
                .Blurb("The callout places the control; what pressing it does is the page's — here, two fields."),
            UiDocsExample.Key("ui-callout-variants").Heading("Variants").File("CalloutVariantsExample.cs").Demo(CalloutVariantsExample).Testid("ui-callout-variants"),
            UiDocsExample.Key("ui-callout-colors").Heading("Colors").File("CalloutColorsExample.cs").Demo(CalloutColorsExample).Testid("ui-callout-colors"),
            UiDocsExample.Key("ui-callout-examples").Heading("Examples").File("CalloutSpotlightsExample.cs").Demo(CalloutSpotlightsExample).Testid("ui-callout-examples")
        ];
}
