namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AccordionExample : Component
{
    protected override Component? Render() =>
        Ui.Accordion[
            Ui.AccordionItem[
                Ui.AccordionHeading["What's your refund policy?"],
                Ui.AccordionContent[
                    "If you are not satisfied with your purchase, we offer a 30-day money-back guarantee. Please contact our support team for assistance."
                ]
            ],
            Ui.AccordionItem[
                Ui.AccordionHeading["Do you offer any discounts for bulk purchases?"],
                Ui.AccordionContent[
                    "Yes, we offer special discounts for bulk orders. Please reach out to our sales team with your requirements."
                ]
            ],
            Ui.AccordionItem[
                Ui.AccordionHeading["How do I track my order?"],
                Ui.AccordionContent[
                    "Once your order is shipped, you will receive an email with a tracking number. Use this number to track your order on our website."
                ]
            ]
        ];
}
