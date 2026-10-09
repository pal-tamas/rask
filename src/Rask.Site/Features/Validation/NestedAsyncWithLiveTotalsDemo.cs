using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Site.Features;

public sealed partial class NestedAsyncWithLiveTotalsDemo : Component
{
    // Layers two things on top of the basic nested-binding showcase:
    //   * Async inline Validate: on a nested field (Address.PostalCode). The field is Live, so the
    //     lookup runs as the code is typed and its kit field shows "Checking…" meanwhile — proves the
    //     latest-wins cancellation + pending-indicator path works for sub-objects, not just root fields.
    //   * Live derived UI: the order totals are computed inside Render() from the current model
    //     state. The promo code, the quantities and the prices are Live, so each is written as it is
    //     typed and the figures follow. The names wait for Pay, as a bound field does unless it says
    //     otherwise. No StateHasChanged calls needed — the dispatcher handles it.
    private static readonly HashSet<string> UndeliverableZips =
        new(StringComparer.Ordinal) { "00000", "99999" };

    private static readonly Dictionary<string, decimal> PromoCodes =
        new(StringComparer.OrdinalIgnoreCase) { ["SAVE10"] = 0.10m, ["SAVE25"] = 0.25m };

    private readonly StorefrontModel _model = new()
    {
        CustomerName = "",
        Address = new StorefrontAddress { PostalCode = "" },
        Items =
        {
            new StorefrontLineItem { Name = "Widget", Quantity = 1, UnitPrice = 9.99m },
            new StorefrontLineItem { Name = "Gadget", Quantity = 2, UnitPrice = 14.99m }
        },
        DiscountCode = ""
    };

    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    private static async ValueTask<IEnumerable<string>> ValidatePostalAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new[] { "Postal code is required." };
        }

        if (!FiveDigits().IsMatch(code))
        {
            return new[] { "Postal code must be 5 digits." };
        }

        // Fake reverse-geocode lookup — the 300ms delay is what drives latest-wins cancellation
        // when the user keeps typing past a partial match. ConfigureAwait(false) is required:
        // the inline async-validator path runs inside HandlerSyncContext, and a captured
        // continuation here would race the outer InvokeWithRenderingAsync mid-await render
        // (concurrent WebSocket.SendAsync calls deadlock on the same socket).
        await Task.Delay(300, Current.Cancellation).ConfigureAwait(false);
        return UndeliverableZips.Contains(code)
            ? new[] { "We don't ship to this area." }
            : Array.Empty<string>();
    }

    protected override Component? Render()
    {
        // Live derived state — recomputed on every render. The dispatcher re-renders this
        // component after each event handler completes, so the figures stay in sync with the
        // model without any explicit subscription.
        var subtotal = _model.Items.Sum(i => i.Quantity * i.UnitPrice);
        var discountPct = PromoCodes.TryGetValue(_model.DiscountCode ?? "", out var p) ? p : 0m;
        var discount = Math.Round(subtotal * discountPct, 2);
        var afterDiscount = subtotal - discount;
        var tax = Math.Round(afterDiscount * 0.08m, 2);
        var total = afterDiscount + tax;

        return
        [
            Form.Model(_model)
                .OnSubmit(m =>
                    _submission = $"Charged ${total.ToString("F2", CultureInfo.InvariantCulture)} to {m.CustomerName}")
                .Class("flex flex-col gap-3")[
                Div[
                    Ui.Input.Bind(() => _model.CustomerName).Label("Customer name")
                        .Id("v-nlive-name")
                        .Validate(v =>
                            string.IsNullOrWhiteSpace(v)
                                ? ["Name is required."]
                                : []).ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => _model.CustomerName)
                ],
                Ui.Input.Bind(() => _model.Address.PostalCode)
                    .Live()
                    .Label("Postal code")
                    .Description("Try 12345, 99999, or any 5-digit code.")
                    .Id("v-nlive-postal")
                    .Validate(ValidatePostalAsync),
                ItemsPanel(),
                Div[
                    Ui.Input.Bind(() => _model.DiscountCode)
                        .Live()
                        .Label("Promo code")
                        .Description("Try SAVE10 or SAVE25.")
                        .Id("v-nlive-promo")
                ],
                Totals(subtotal, discountPct, discount, tax, total),
                Div[
                    Ui.Button.Primary.Icon(Ui.IconName.CreditCard).Submit["Pay"]
                ]
            ],
            _submission is null
                ? null
                : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Id("v-nlive-submission").Role("status").Text(_submission)
        ];
    }

    private Component ItemsPanel() =>
        Div.Class("border rounded p-3")[
            Div.Class("font-semibold text-sm mb-2")["Items"],
            Div.Class("grid grid-cols-12 gap-4 mb-2 items-center")[
                Div.Class("col-span-6")[
                    Ui.Input.Bind(() => _model.Items[0].Name)
                        .Label("Item 1 name")
                        .Id("v-nlive-item0-name")
                ],
                Div.Class("col-span-3")[
                    Ui.Input.Bind(() => _model.Items[0].Quantity)
                        .Live()
                        .Label("Item 1 quantity")
                        .Id("v-nlive-item0-qty")
                        .Min("0")
                ],
                Div.Class("col-span-3")[
                    Ui.Input.Bind(() => _model.Items[0].UnitPrice)
                        .Live()
                        .Label("Item 1 unit price")
                        .Id("v-nlive-item0-price")
                        .Step("0.01")
                ]
            ],
            Div.Class("grid grid-cols-12 gap-4 items-center")[
                Div.Class("col-span-6")[
                    Ui.Input.Bind(() => _model.Items[1].Name)
                        .Label("Item 2 name")
                        .Id("v-nlive-item1-name")
                ],
                Div.Class("col-span-3")[
                    Ui.Input.Bind(() => _model.Items[1].Quantity)
                        .Live()
                        .Label("Item 2 quantity")
                        .Id("v-nlive-item1-qty")
                        .Min("0")
                ],
                Div.Class("col-span-3")[
                    Ui.Input.Bind(() => _model.Items[1].UnitPrice)
                        .Live()
                        .Label("Item 2 unit price")
                        .Id("v-nlive-item1-price")
                        .Step("0.01")
                ]
            ]
        ];

    private static Component Totals(decimal subtotal, decimal discountPct, decimal discount, decimal tax, decimal total) =>
        Div.Id("v-nlive-totals").Class("bg-ui-well rounded p-3 text-sm")[
            Div.Class("flex justify-between flex-wrap items-center")[
                Span["Subtotal"],
                Span.Id("v-nlive-subtotal")[$"${subtotal.ToString("F2", CultureInfo.InvariantCulture)}"]
            ],
            Div.Class("flex justify-between flex-wrap items-center")[
                Span[discountPct > 0m
                    ? $"Discount ({(int)(discountPct * 100)}%)"
                    : "Discount"],
                Span.Id("v-nlive-discount")[$"-${discount.ToString("F2", CultureInfo.InvariantCulture)}"]
            ],
            Div.Class("flex justify-between flex-wrap items-center")[
                Span["Tax (8%)"],
                Span.Id("v-nlive-tax")[$"${tax.ToString("F2", CultureInfo.InvariantCulture)}"]
            ],
            Hr.Class("my-2"),
            Div.Class("flex justify-between flex-wrap items-center font-bold")[
                Span["Total"],
                Span.Id("v-nlive-total")[$"${total.ToString("F2", CultureInfo.InvariantCulture)}"]
            ]
        ];

    [GeneratedRegex(@"^\d{5}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FiveDigits();
}
