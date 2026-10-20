namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AccordionExclusiveExample : Component
{
    protected override Component? Render() =>
        Ui.Accordion.Exclusive()[
            Ui.AccordionItem.Heading("What's your refund policy?")[
                "If you are not satisfied with your purchase, we offer a 30-day money-back guarantee. Please contact our support team for assistance."
            ],
            Ui.AccordionItem.Heading("Do you offer any discounts for bulk purchases?")[
                "Yes, we offer special discounts for bulk orders. Please reach out to our sales team with your requirements."
            ],
            Ui.AccordionItem.Heading("How do I track my order?")[
                "Once your order is shipped, you will receive an email with a tracking number. Use this number to track your order on our website."
            ]
        ];
}
