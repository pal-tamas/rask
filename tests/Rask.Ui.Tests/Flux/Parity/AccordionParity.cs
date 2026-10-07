using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/accordion</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     "Shorthand" has no rendered example on Flux's page; every example from "Findable content" on is
///     written with it here, as it is there.
///     </para>
///     <para>
///     Flux draws a <c>&lt;ui-disclosure&gt;</c> custom element around a <c>&lt;button&gt;</c> and opens it
///     with a script. The kit ships no script, so an item is the platform's own disclosure: a
///     <c>&lt;details&gt;</c> around a <c>&lt;summary&gt;</c>. Those three tags are the only difference the
///     tool is told to expect.
///     </para>
/// </remarks>
public sealed partial class AccordionParity : FluxParity
{
    private const string Refund =
        "If you are not satisfied with your purchase, we offer a 30-day money-back guarantee. Please contact our support team for assistance.";

    private const string Bulk =
        "Yes, we offer special discounts for bulk orders. Please reach out to our sales team with your requirements.";

    private const string Tracking =
        "Once your order is shipped, you will receive an email with a tracking number. Use this number to track your order on our website.";

    public override string Page => "accordion";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Column(
            Ui.Accordion[
                Ui.AccordionItem[
                    Ui.AccordionHeading["What's your refund policy?"],
                    Ui.AccordionContent[Refund]
                ],
                Ui.AccordionItem[
                    Ui.AccordionHeading["Do you offer any discounts for bulk purchases?"],
                    Ui.AccordionContent[Bulk]
                ],
                Ui.AccordionItem[
                    Ui.AccordionHeading["How do I track my order?"],
                    Ui.AccordionContent[Tracking]
                ]
            ]));

        yield return ("with-transition", Column(Ui.Accordion.Transition()[Questions()]));

        yield return ("findable-content", Column(
            Ui.Accordion[
                Ui.AccordionItem.Heading("Where do you ship?")["We ship to addresses throughout the United States and Canada."],
                Ui.AccordionItem.Heading("Do I need to be home for delivery?")["Orders over $500 require a signature upon delivery."],
                Ui.AccordionItem.Heading("Can I change my order?")["Contact our support team before your order has shipped."]
            ]));

        yield return ("disabled", Column(
            Ui.Accordion[
                Ui.AccordionItem.Heading("What's your refund policy?")["It all depends how nice you are to me in your email."],
                Ui.AccordionItem.Heading("Do you offer PPP discounts?").Disabled()[Bulk],
                Ui.AccordionItem.Heading("How do I track my order?")["What do YOU think?"]
            ]));

        yield return ("exclusive", Column(Ui.Accordion.Exclusive()[Questions()]));

        // The one example measured with an item open: the second, as Flux's page loads it.
        yield return ("expanded", Column(Ui.Accordion[Questions(expanded: true)]));

        yield return ("leading-icon", Column(Ui.Accordion.Reverse[Questions()]));
    }

    private static Component[] Questions(bool expanded = false) =>
    [
        Ui.AccordionItem.Heading("What's your refund policy?")[Refund],
        Ui.AccordionItem.Heading("Do you offer any discounts for bulk purchases?").Expanded(expanded)[Bulk],
        Ui.AccordionItem.Heading("How do I track my order?")[Tracking]
    ];

    // The column Flux's docs page sets an example in.
    private static Component Column(Component accordion) =>
        Div.Style("max-width:384px;margin:0 auto")[accordion];
}
