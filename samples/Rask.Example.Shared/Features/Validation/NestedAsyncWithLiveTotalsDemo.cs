using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Example.Shared.Features;

public sealed partial class NestedAsyncWithLiveTotalsDemo : Component
{
    // Layers two things on top of the basic nested-binding showcase:
    //   * Async inline Validate: on a nested field (Address.PostalCode) with ValidatingIndicator —
    //     proves the latest-wins cancellation + pending-indicator path works for sub-objects, not
    //     just root fields.
    //   * Live derived UI: the order totals are computed inside Render() from the current model
    //     state. Every event handler re-renders the owning component, so the figures update on
    //     each keystroke (string discount code, OnInput) and on each blur (int/decimal qty/price,
    //     OnChange). No StateHasChanged calls needed — the dispatcher handles it.
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
        [.. msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            BsIcon(Name: BsIconName.ArrowClockwise, Class: "me-1"), "Checking delivery zone…"
        ];

    private static async ValueTask<IEnumerable<string>> ValidatePostalAsync(
        string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new[] { "Postal code is required." };
        }

        if (!Regex.IsMatch(code, @"^\d{5}$"))
        {
            return new[] { "Postal code must be 5 digits." };
        }

        // Fake reverse-geocode lookup — the 300ms delay is what drives latest-wins cancellation
        // when the user keeps typing past a partial match. ConfigureAwait(false) is required:
        // the inline async-validator path runs inside HandlerSyncContext, and a captured
        // continuation here would race the outer InvokeWithRenderingAsync mid-await render
        // (concurrent WebSocket.SendAsync calls deadlock on the same socket).
        await Task.Delay(300, ct).ConfigureAwait(false);
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
            Form(
                _model,
                m => _submission = $"Charged ${total.ToString("F2", CultureInfo.InvariantCulture)} to {m.CustomerName}",
                Class: "vstack gap-3")[
                Div()[
                    Label("v-nlive-name", Class: "form-label small mb-1")["Customer name"],
                    Input(() => _model.CustomerName, Id: "v-nlive-name", Class: "form-control",
                        Validate: v =>
                            string.IsNullOrWhiteSpace(v)
                                ? new[] { "Name is required." }
                                : Array.Empty<string>()),
                    ValidationMessage(() => _model.CustomerName, FieldError)
                ],
                Div()[
                    Label("v-nlive-postal", Class: "form-label small mb-1")[
                        "Postal code ", Span(Class: "text-muted")["(try 12345, 99999, or any 5-digit code)"]
                    ],
                    Input(() => _model.Address.PostalCode, Id: "v-nlive-postal", Class: "form-control",
                        Validate: ValidatePostalAsync),
                    ValidatingIndicator(() => _model.Address.PostalCode, Checking),
                    ValidationMessage(() => _model.Address.PostalCode, FieldError)
                ],
                Div(Class: "border rounded p-3")[
                    Div(Class: "fw-semibold small mb-2")["Items"],
                    BsRow(Gutter: 2, Class: Bs.Join(Margin.Bottom(2), Flex.Align(BsAlign.Center)))[
                        BsCol(Span: 6)[
                            Input(() => _model.Items[0].Name,
                                Id: "v-nlive-item0-name", Class: "form-control form-control-sm")
                        ],
                        BsCol(Span: 3)[
                            Input(() => _model.Items[0].Quantity,
                                Id: "v-nlive-item0-qty", Class: "form-control form-control-sm", Min: "0")
                        ],
                        BsCol(Span: 3)[
                            Input(() => _model.Items[0].UnitPrice,
                                Id: "v-nlive-item0-price", Class: "form-control form-control-sm", Step: "0.01")
                        ]
                    ],
                    BsRow(Gutter: 2, Class: Flex.Align(BsAlign.Center))[
                        BsCol(Span: 6)[
                            Input(() => _model.Items[1].Name,
                                Id: "v-nlive-item1-name", Class: "form-control form-control-sm")
                        ],
                        BsCol(Span: 3)[
                            Input(() => _model.Items[1].Quantity,
                                Id: "v-nlive-item1-qty", Class: "form-control form-control-sm", Min: "0")
                        ],
                        BsCol(Span: 3)[
                            Input(() => _model.Items[1].UnitPrice,
                                Id: "v-nlive-item1-price", Class: "form-control form-control-sm", Step: "0.01")
                        ]
                    ]
                ],
                Div()[
                    Label("v-nlive-promo", Class: "form-label small mb-1")[
                        "Promo code ", Span(Class: "text-muted")["(try SAVE10 or SAVE25)"]
                    ],
                    Input(() => _model.DiscountCode, Id: "v-nlive-promo", Class: "form-control")
                ],
                Div(Id: "v-nlive-totals", Class: "bg-light rounded p-3 small")[
                    BsStack(Justify: BsJustify.Between)[
                        Span()["Subtotal"],
                        Span("v-nlive-subtotal")[$"${subtotal.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ],
                    BsStack(Justify: BsJustify.Between)[
                        Span()[discountPct > 0m
                            ? $"Discount ({(int)(discountPct * 100)}%)"
                            : "Discount"],
                        Span("v-nlive-discount")[$"-${discount.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ],
                    BsStack(Justify: BsJustify.Between)[
                        Span()["Tax (8%)"],
                        Span("v-nlive-tax")[$"${tax.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ],
                    Hr(Class: "my-2"),
                    BsStack(Justify: BsJustify.Between, Class: Font.Bold)[
                        Span()["Total"],
                        Span("v-nlive-total")[$"${total.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ]
                ],
                Div()[
                    BsButton(Type: "submit", Color: BsColor.Primary)[BsIcon(Name: BsIconName.CreditCard, Class: "me-1"), "Pay"]
                ]
            ],
            _submission is null
                ? null
                : Div(Id: "v-nlive-submission", Class: "alert alert-success small mt-3 mb-0")[
                    BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _submission
                ]
        ];
    }
}

public sealed class StorefrontModel
{
    public string CustomerName { get; set; } = "";
    public StorefrontAddress Address { get; set; } = new();
    public List<StorefrontLineItem> Items { get; set; } = new();
    public string DiscountCode { get; set; } = "";
}

public sealed class StorefrontAddress
{
    public string PostalCode { get; set; } = "";
}

public sealed class StorefrontLineItem
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
