using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's accordion as a C# component: each example with its own code, and its reference.</summary>
[Route("accordion")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiAccordionPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Accordion.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Accordion)
            .Lead("A stack of items, each a heading that opens the content under it. Every item is a <details>, so it "
                  + "opens with a click, Enter or Space in the browser, with no handler and nothing sent to the page.")
            .Api(UiApi.Accordion)[
            UiDocsExample.Key("ui-accordion-basic").Heading("Accordion").File("AccordionExample.cs").Demo(AccordionExample).Testid("ui-accordion-basic")
                .Blurb("Items of a heading and its content."),
            UiDocsExample.Key("ui-accordion-shorthand").Heading("Shorthand").File("AccordionShorthandExample.cs").Demo(AccordionShorthandExample).Testid("ui-accordion-shorthand")
                .Blurb("Heading on the item says the heading; the children are the content."),
            UiDocsExample.Key("ui-accordion-transition").Heading("With transition").File("AccordionTransitionExample.cs").Demo(AccordionTransitionExample).Testid("ui-accordion-transition")
                .Blurb("Transition() opens and closes each item over a quarter of a second."),
            UiDocsExample.Key("ui-accordion-findable").Heading("Findable content").File("AccordionFindableExample.cs").Demo(AccordionFindableExample).Testid("ui-accordion-findable")
                .Blurb("Search the page for \"signature\": the browser opens the item holding it."),
            UiDocsExample.Key("ui-accordion-disabled").Heading("Disabled").File("AccordionDisabledExample.cs").Demo(AccordionDisabledExample).Testid("ui-accordion-disabled")
                .Blurb("A disabled item cannot be opened, and Tab passes over it."),
            UiDocsExample.Key("ui-accordion-exclusive").Heading("Exclusive").File("AccordionExclusiveExample.cs").Demo(AccordionExclusiveExample).Testid("ui-accordion-exclusive")
                .Blurb("Opening one item closes the others."),
            UiDocsExample.Key("ui-accordion-expanded").Heading("Expanded").File("AccordionExpandedExample.cs").Demo(AccordionExpandedExample).Testid("ui-accordion-expanded")
                .Blurb("Expanded() is open as the page loads."),
            UiDocsExample.Key("ui-accordion-reverse").Heading("Leading icon").File("AccordionLeadingIconExample.cs").Demo(AccordionLeadingIconExample).Testid("ui-accordion-reverse")
                .Blurb("Reverse puts the chevron before the heading."),
            UiAccordionBehaviour
        ];
}
