namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AccordionDisabledExample : Component
{
    protected override Component? Render() =>
        Ui.Accordion[
            Ui.AccordionItem.Heading("What's your refund policy?")["It all depends how nice you are to me in your email."],
            Ui.AccordionItem.Heading("Do you offer PPP discounts?").Disabled()[
                "Yes, we offer special discounts for bulk orders. Please reach out to our sales team with your requirements."
            ],
            Ui.AccordionItem.Heading("How do I track my order?")["What do YOU think?"]
        ];
}
