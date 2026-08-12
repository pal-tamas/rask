namespace Rask.Example.Shop.Features.Orders;

/// <summary>
/// An order confirmation email body — a Rask component rendered to HTML by <c>Email.Body(...)</c>.
/// </summary>
/// <remarks>
/// The point of the pillar: an email body is written with the same components as a page, so it is typed,
/// refactorable, and HTML-encoded by the same renderer. Inline styles rather than a stylesheet, because
/// mail clients strip <c>&lt;style&gt;</c> blocks.
/// </remarks>
public sealed partial class OrderConfirmation : Component
{
    /// <summary>Who placed the order.</summary>
    public string? Customer { get; set; }

    /// <summary>What it came to.</summary>
    public decimal Total { get; set; }

    protected override Component? Render() =>
        Div.Style("font-family:system-ui,sans-serif;max-width:32rem")[
            H1.Style("font-size:1.25rem")["Thanks for your order"],
            P[$"We've got your order, {Customer}."],
            P[
                "Total: ",
                Strong[Total.ToString("C", System.Globalization.CultureInfo.InvariantCulture)]
            ],
            Hr,
            P.Style("color:#666;font-size:0.875rem")[
                "This message was queued on the app's own database and delivered by a background worker."
            ]
        ];
}
