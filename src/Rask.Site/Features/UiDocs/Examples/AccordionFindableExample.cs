namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AccordionFindableExample : Component
{
    protected override Component? Render() =>
        Ui.Accordion[
            Ui.AccordionItem.Heading("Where do you ship?")["We ship to addresses throughout the United States and Canada."],
            Ui.AccordionItem.Heading("Do I need to be home for delivery?")["Orders over $500 require a signature upon delivery."],
            Ui.AccordionItem.Heading("Can I change my order?")["Contact our support team before your order has shipped."]
        ];
}
