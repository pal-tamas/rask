using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Demos;

public sealed class ValidationFieldsDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render() =>
        [
            Form<RegistrationModel>(
                _model,
                m => _submission = $"Registered: {m.Name} <{m.Email}>",
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                Div()[
                    Label("v1-name", Class: "form-label small mb-1")["Name"],
                    Input(() => _model.Name, Id: "v1-name", Class: "form-control"),
                    ValidationMessage(() => _model.Name, FieldError)
                ],
                Div()[
                    Label("v1-email", Class: "form-label small mb-1")["Email"],
                    Input(() => _model.Email, Id: "v1-email", Type: "email",
                        Class: "form-control"),
                    ValidationMessage(() => _model.Email, FieldError)
                ],
                Div()[
                    Label("v1-age", Class: "form-label small mb-1")["Age"],
                    Input(() => _model.Age, Id: "v1-age", Class: "form-control"),
                    ValidationMessage(() => _model.Age, FieldError)
                ],
                Div()[
                    Label("v1-plan", Class: "form-label small mb-1")["Plan"],
                    Select(() => _model.Plan, Id: "v1-plan", Class: "form-select")[
                        Option("")["— choose —"],
                        Option("free")["Free"],
                        Option("pro")["Pro"],
                        Option("team")["Team"]
                    ],
                    ValidationMessage(() => _model.Plan, FieldError)
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Register"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class ValidationSummaryDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        Div(Class: "alert alert-danger small mb-0")[
            Div(Class: "fw-semibold mb-1")[
                I(Class: "bi bi-exclamation-triangle me-1"),
                $"Please fix {entries.Count} error{(entries.Count == 1 ? "" : "s")}:"
            ],
            Ul(Class: "mb-0 ps-3")[
                entries.Select((e, i) => (Child)Li(Key: i)[
                    e.Field.Length == 0
                        ? (Child)e.Message
                        : (Child)Fragment()[Strong()[e.Field], ": ", e.Message]
                ])
            ]
        ];

    protected override RenderResult Render() =>
        [
            Form<RegistrationModel>(
                _model,
                m => _submission = $"Registered: {m.Name} <{m.Email}>",
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                ValidationSummary(SummaryAlert),
                Div()[
                    Label("v2-name", Class: "form-label small mb-1")["Name"],
                    Input(() => _model.Name, Id: "v2-name", Class: "form-control")
                ],
                Div()[
                    Label("v2-email", Class: "form-label small mb-1")["Email"],
                    Input(() => _model.Email, Id: "v2-email", Type: "email",
                        Class: "form-control")
                ],
                Div()[
                    Label("v2-age", Class: "form-label small mb-1")["Age"],
                    Input(() => _model.Age, Id: "v2-age", Class: "form-control")
                ],
                Div()[
                    Label("v2-plan", Class: "form-label small mb-1")["Plan"],
                    Select(() => _model.Plan, Id: "v2-plan", Class: "form-select")[
                        Option("")["— choose —"],
                        Option("free")["Free"],
                        Option("pro")["Pro"],
                        Option("team")["Team"]
                    ]
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Register"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class InlineValidateDemo : Component
{
    private readonly LoginModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        // Filter to form-level entries — per-field rules already render through FieldError.
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return Fragment();
        }

        return Div(Class: "alert alert-danger small mb-0")[
            Ul(Class: "mb-0 ps-3")[
                formOnly.Select((e, i) => (Child)Li(Key: i)[e.Message])
            ]
        ];
    }

    protected override RenderResult Render() =>
        [
            Form(
                _model,
                OnValidSubmit: m => _submission = $"Welcome, {m.Email}",
                Class: "vstack gap-3",
                Validate: m =>
                    m.Password == m.Confirm ? Array.Empty<string>() : new[] { "Passwords do not match." })[
                Div()[
                    Label("v4-email", Class: "form-label small mb-1")["Email"],
                    Input(() => _model.Email, Id: "v4-email", Type: "email", Class: "form-control",
                        Validate: v =>
                            v.Contains('@')
                                ? Array.Empty<string>()
                                : new[] { "Email looks wrong." }),
                    ValidationMessage(() => _model.Email, FieldError)
                ],
                Div()[
                    Label("v4-password", Class: "form-label small mb-1")["Password"],
                    Input(() => _model.Password, Id: "v4-password", Type: "password", Class: "form-control")
                ],
                Div()[
                    Label("v4-confirm", Class: "form-label small mb-1")["Confirm"],
                    Input(() => _model.Confirm, Id: "v4-confirm", Type: "password", Class: "form-control")
                ],
                ValidationSummary(SummaryAlert),
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Sign in"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class LoginModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Confirm { get; set; } = "";
}

public sealed class NestedAsyncWithLiveTotalsDemo : Component
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
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            I(Class: "bi bi-arrow-clockwise me-1"), "Checking delivery zone…"
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

    protected override RenderResult Render()
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

        return [
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
                    Div(Class: "row g-2 mb-2 align-items-center")[
                        Div(Class: "col-6")[
                            Input(() => _model.Items[0].Name,
                                Id: "v-nlive-item0-name", Class: "form-control form-control-sm")
                        ],
                        Div(Class: "col-3")[
                            Input(() => _model.Items[0].Quantity,
                                Id: "v-nlive-item0-qty", Class: "form-control form-control-sm", Min: "0")
                        ],
                        Div(Class: "col-3")[
                            Input(() => _model.Items[0].UnitPrice,
                                Id: "v-nlive-item0-price", Class: "form-control form-control-sm", Step: "0.01")
                        ]
                    ],
                    Div(Class: "row g-2 align-items-center")[
                        Div(Class: "col-6")[
                            Input(() => _model.Items[1].Name,
                                Id: "v-nlive-item1-name", Class: "form-control form-control-sm")
                        ],
                        Div(Class: "col-3")[
                            Input(() => _model.Items[1].Quantity,
                                Id: "v-nlive-item1-qty", Class: "form-control form-control-sm", Min: "0")
                        ],
                        Div(Class: "col-3")[
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
                    Div(Class: "d-flex justify-content-between")[
                        Span()["Subtotal"],
                        Span("v-nlive-subtotal")[$"${subtotal.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ],
                    Div(Class: "d-flex justify-content-between")[
                        Span()[discountPct > 0m
                            ? $"Discount ({(int)(discountPct * 100)}%)"
                            : "Discount"],
                        Span("v-nlive-discount")[$"-${discount.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ],
                    Div(Class: "d-flex justify-content-between")[
                        Span()["Tax (8%)"],
                        Span("v-nlive-tax")[$"${tax.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ],
                    Hr(Class: "my-2"),
                    Div(Class: "d-flex justify-content-between fw-bold")[
                        Span()["Total"],
                        Span("v-nlive-total")[$"${total.ToString("F2", CultureInfo.InvariantCulture)}"]
                    ]
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-credit-card me-1"), "Pay"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Id: "v-nlive-submission", Class: "alert alert-success small mt-3 mb-0")[
                    I(Class: "bi bi-check-circle me-2"), _submission
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

public sealed class InlineAsyncValidateDemo : Component
{
    // Showcases the typed async Validate overload: a bare `async (v, ct) => …` lambda binds
    // directly to Func<TProp, CancellationToken, ValueTask<IEnumerable<string>>> on the Input,
    // and a bare `async (m, ct) => …` lambda binds the same shape on Form — both with no cast.
    // The 250ms delay drives the latest-wins cancellation path (rapid typing supersedes the
    // prior in-flight run) and ValidatingIndicator surfaces the pending state.
    private static readonly HashSet<string> TakenCodes =
        new(StringComparer.OrdinalIgnoreCase) { "BAD-001", "DEAD-BEEF", "RESERVED" };

    private readonly PromoModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            I(Class: "bi bi-arrow-clockwise me-1"), "Checking…"
        ];

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return Fragment();
        }

        return Div(Class: "alert alert-danger small mb-0")[
            Ul(Class: "mb-0 ps-3")[formOnly.Select((e, i) => (Child)Li(Key: i)[e.Message])]
        ];
    }

    private static async ValueTask<IEnumerable<string>> CheckCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Array.Empty<string>();
        }

        await Task.Delay(250, ct).ConfigureAwait(false);
        return TakenCodes.Contains(code) ? new[] { $"\"{code}\" is reserved." } : Array.Empty<string>();
    }

    protected override RenderResult Render() =>
        [
            Form<PromoModel>(
                _model,
                OnValidSubmit: m => _submission = $"Redeemed: {m.Code}",
                Class: "vstack gap-3",
                Validate: async (m, ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return string.IsNullOrWhiteSpace(m.Code)
                        ? new[] { "Code is required." }
                        : Array.Empty<string>();
                })[
                Div()[
                    Label("v10-code", Class: "form-label small mb-1")["Promo code"],
                    Input(() => _model.Code, Id: "v10-code", Class: "form-control",
                        Validate: CheckCodeAsync),
                    ValidatingIndicator(() => _model.Code, Checking),
                    ValidationMessage(() => _model.Code, FieldError)
                ],
                ValidationSummary(SummaryAlert),
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-gift me-1"), "Redeem"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class PromoModel
{
    public string Code { get; set; } = "";
}

public sealed class AsyncValidationDemo : Component
{
    private readonly EditContext _ctx;
    private readonly SignupModel _model = new();
    private string? _submission;

    public AsyncValidationDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new UniqueUsernameValidator());
    }

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            I(Class: "bi bi-arrow-clockwise me-1"), "Checking availability..."
        ];

    protected override RenderResult Render() =>
        [
            Form<SignupModel>(
                _model,
                m => _submission = $"Signed up: {m.Username}",
                Context: _ctx,
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                Div()[
                    Label("v3-username", Class: "form-label small mb-1")["Username"],
                    Input(() => _model.Username, Id: "v3-username", Class: "form-control"),
                    ValidatingIndicator(() => _model.Username, Checking),
                    ValidationMessage(() => _model.Username,
                        msgs => Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])])
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Sign up"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class SignupModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Username must be 3–20 characters.")]
    public string Username { get; set; } = "";
}

public sealed class UniqueUsernameValidator : IAsyncFieldValidator
{
    private static readonly HashSet<string> Taken = new(StringComparer.OrdinalIgnoreCase) { "admin", "taken", "root" };

    public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
    {
        if (context.Model is SignupModel m)
        {
            await CheckAsync(context, new FieldIdentifier(m, nameof(SignupModel.Username)), m.Username,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
        CancellationToken cancellationToken)
    {
        if (context.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
        {
            await CheckAsync(context, field, m.Username, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CheckAsync(EditContext context, FieldIdentifier field, string username,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        // E2E test seam: the literal "explode" forces the validator to throw mid-await so the
        // framework's generic "Validation could not be completed." path is exercised end-to-end.
        if (string.Equals(username, "explode", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Yield();
            throw new InvalidOperationException("Simulated remote failure.");
        }

        await Task.Delay(400, ct).ConfigureAwait(false);
        if (Taken.Contains(username))
        {
            context.AddValidationMessage(field, $"\"{username}\" is already taken.");
        }
    }
}

public sealed class RegistrationModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "Name must be 2–40 characters.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Range(13, 120, ErrorMessage = "Age must be between 13 and 120.")]
    public int Age { get; set; }

    [Required(ErrorMessage = "Pick a plan.")]
    public string Plan { get; set; } = "";
}

public sealed class CrossFieldSummaryDemo : Component
{
    private readonly TripModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        entries.Count == 0
            ? Fragment()
            : Div(Class: "alert alert-danger small mb-0")[
                Ul(Class: "mb-0 ps-3")[
                    entries.Select((e, i) => (Child)Li(Key: i)[
                        e.Field.Length == 0
                            ? (Child)e.Message
                            : (Child)Fragment()[Strong()[e.Field], ": ", e.Message]
                    ])
                ]
            ];

    protected override RenderResult Render() =>
        [
            Form<TripModel>(
                _model,
                OnValidSubmit: m => _submission = $"Booked: {m.Depart:yyyy-MM-dd} → {m.Return:yyyy-MM-dd}",
                Class: "vstack gap-3",
                Validate: m =>
                    m.Return > m.Depart
                        ? Array.Empty<string>()
                        : new[] { "Return date must be after departure." })[
                ValidationSummary(SummaryAlert),
                Div()[
                    Label("v5-depart", Class: "form-label small mb-1")["Departure"],
                    Input(() => _model.Depart, Id: "v5-depart", Class: "form-control")
                ],
                Div()[
                    Label("v5-return", Class: "form-label small mb-1")["Return"],
                    Input(() => _model.Return, Id: "v5-return", Class: "form-control")
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-airplane me-1"), "Book"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class TripModel
{
    public DateOnly Depart { get; set; } = new(2026, 6, 1);
    public DateOnly Return { get; set; } = new(2026, 6, 1);
}

// IValidatableObject parity with ASP.NET Core: BookingModel mixes attribute rules ([Required]
// on Name) with an IValidatableObject.Validate method that yields both a per-field result
// (MemberNames=[nameof(Departure)]) and a form-level result (no MemberNames). The BCL's own
// Validator.TryValidateObject would silence Validate() once the attribute fails — Rask's
// DataAnnotationsValidator calls IValidatableObject directly so all errors accumulate.
public sealed class ValidatableObjectDemo : Component
{
    private readonly BookingModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return Fragment();
        }

        return Div(Class: "alert alert-danger small mb-0")[
            Ul(Class: "mb-0 ps-3")[formOnly.Select((e, i) => (Child)Li(Key: i)[e.Message])]
        ];
    }

    protected override RenderResult Render() =>
        [
            Form<BookingModel>(
                _model,
                m => _submission = $"Booked: {m.Name} {m.Departure:yyyy-MM-dd} → {m.Arrival:yyyy-MM-dd}",
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                ValidationSummary(SummaryAlert),
                Div()[
                    Label("v11-name", Class: "form-label small mb-1")["Name"],
                    Input(() => _model.Name, Id: "v11-name", Class: "form-control"),
                    ValidationMessage(() => _model.Name, FieldError)
                ],
                Div()[
                    Label("v11-departure", Class: "form-label small mb-1")["Departure"],
                    Input(() => _model.Departure, Id: "v11-departure", Class: "form-control"),
                    ValidationMessage(() => _model.Departure, FieldError)
                ],
                Div()[
                    Label("v11-arrival", Class: "form-label small mb-1")["Arrival"],
                    Input(() => _model.Arrival, Id: "v11-arrival", Class: "form-control"),
                    ValidationMessage(() => _model.Arrival, FieldError)
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-calendar-check me-1"), "Book"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class BookingModel : IValidatableObject
{
    private static readonly DateOnly Today = new(2026, 5, 14);

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = "";

    public DateOnly Departure { get; set; } = new(2026, 7, 1);
    public DateOnly Arrival { get; set; } = new(2026, 7, 5);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Departure < Today)
        {
            yield return new ValidationResult(
                "Departure cannot be in the past.",
                new[] { nameof(Departure) });
        }

        if (Arrival <= Departure)
        {
            yield return new ValidationResult("Arrival must be after departure.");
        }
    }
}

public sealed class ProgrammaticValidateDemo : Component
{
    private readonly EditContext _ctx;
    private readonly TaskModel _model = new();
    private string? _submission;

    public ProgrammaticValidateDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new SlowTitleValidator());
    }

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            I(Class: "bi bi-arrow-clockwise me-1"), "Checking…"
        ];

    private async Task ValidateNowAsync() => await _ctx.ValidateAsync().ConfigureAwait(false);

    protected override RenderResult Render() =>
        [
            Form<TaskModel>(
                _model,
                m => _submission = $"Saved task: {m.Title}",
                Context: _ctx,
                Class: "vstack gap-3")[
                Div()[
                    Label("v6-title", Class: "form-label small mb-1")["Title"],
                    Input(() => _model.Title, Id: "v6-title", Class: "form-control"),
                    ValidatingIndicator(() => _model.Title, Checking),
                    ValidationMessage(() => _model.Title, FieldError)
                ],
                Div(Class: "d-flex gap-2")[
                    Button(
                        "button",
                        Id: "v6-validate-now",
                        Class: "btn btn-outline-secondary",
                        OnClickAsync: ValidateNowAsync)[
                        I(Class: "bi bi-search me-1"), "Validate now"
                    ],
                    Button(
                        "submit",
                        Id: "v6-submit",
                        Disabled: _ctx.IsValidatingAny,
                        Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Save"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class TaskModel
{
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = "";
}

// 600ms delay so the e2e test for submit-disable has a deterministic window to observe
// the disabled state before the async validator settles. Like UniqueUsernameValidator,
// the literal "explode" exercises the framework's exception fallback.
public sealed class SlowTitleValidator : IAsyncFieldValidator
{
    public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
    {
        if (context.Model is TaskModel m)
        {
            await CheckAsync(context, new FieldIdentifier(m, nameof(TaskModel.Title)), m.Title, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
        CancellationToken cancellationToken)
    {
        if (context.Model is TaskModel m && field.FieldName == nameof(TaskModel.Title))
        {
            await CheckAsync(context, field, m.Title, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CheckAsync(EditContext context, FieldIdentifier field, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        await Task.Delay(600, ct).ConfigureAwait(false);
        if (string.Equals(title, "duplicate", StringComparison.OrdinalIgnoreCase))
        {
            context.AddValidationMessage(field, $"\"{title}\" is already used.");
        }
    }
}

public sealed class FluentValidationDemo : Component
{
    private readonly OrderModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render() =>
        [
            Form<OrderModel>(
                _model,
                m => _submission = $"Ordered {m.Quantity} × {m.Product}",
                Class: "vstack gap-3")[
                FluentValidationValidator(new OrderValidator()),
                Div()[
                    Label("v7-product", Class: "form-label small mb-1")["Product"],
                    Input(() => _model.Product, Id: "v7-product", Class: "form-control"),
                    ValidationMessage(() => _model.Product, FieldError)
                ],
                Div()[
                    Label("v7-quantity", Class: "form-label small mb-1")["Quantity"],
                    Input(() => _model.Quantity, Id: "v7-quantity", Class: "form-control"),
                    ValidationMessage(() => _model.Quantity, FieldError)
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-bag-check me-1"), "Order"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class OrderModel
{
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class OrderValidator : AbstractValidator<OrderModel>
{
    public OrderValidator()
    {
        RuleFor(x => x.Product).NotEmpty().WithMessage("Product is required.");
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
    }
}

// First-error-wins: an inline per-field rule and a DataAnnotations rule both target the
// same field. EditContext gates later stages once any earlier stage has flagged the field,
// so the inline "Required." message appears while the input is empty, and ONLY after that
// rule passes does the [RegularExpression] format error surface.
public sealed class FirstErrorWinsDemo : Component
{
    private readonly LicenseModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render() =>
        [
            Form<LicenseModel>(
                _model,
                m => _submission = $"Activated: {m.Code}",
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                Div()[
                    Label("v8-code", Class: "form-label small mb-1")["License code"],
                    Input(() => _model.Code, Id: "v8-code", Class: "form-control",
                        Validate: v =>
                            string.IsNullOrWhiteSpace(v)
                                ? new[] { "Code is required." }
                                : Array.Empty<string>()),
                    ValidationMessage(() => _model.Code, FieldError)
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-unlock me-1"), "Activate"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class LicenseModel
{
    [RegularExpression(@"^[A-Z]{3}-\d{3}$", ErrorMessage = "Use the ABC-123 format.")]
    public string Code { get; set; } = "";
}

// FluentValidation async: a single RuleFor chain stacks NotEmpty → Matches → MustAsync.
// FluentValidationValidator wraps the whole IValidator into an IAsyncFieldValidator, so
// MustAsync awaits the network-shaped check and the ValidatingIndicator surfaces while
// the await is in flight.
public sealed class FluentValidationAsyncDemo : Component
{
    private readonly TicketModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            I(Class: "bi bi-arrow-clockwise me-1"), "Checking availability..."
        ];

    protected override RenderResult Render() =>
        [
            Form<TicketModel>(
                _model,
                m => _submission = $"Reserved: {m.Code}",
                Class: "vstack gap-3")[
                FluentValidationValidator(new TicketValidator()),
                Div()[
                    Label("v9-code", Class: "form-label small mb-1")["Ticket code"],
                    Input(() => _model.Code, Id: "v9-code", Class: "form-control"),
                    ValidatingIndicator(() => _model.Code, Checking),
                    ValidationMessage(() => _model.Code, FieldError)
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-ticket-perforated me-1"), "Reserve"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class TicketModel
{
    public string Code { get; set; } = "";
}

// CascadeMode.Stop keeps FV's own chain aligned with Rask's first-error-wins gating:
// NotEmpty must pass before Matches runs, which must pass before MustAsync fires.
public sealed class TicketValidator : AbstractValidator<TicketModel>
{
    private static readonly HashSet<string> Used = new(StringComparer.OrdinalIgnoreCase)
    {
        "TKT-001", "TKT-002", "TKT-003"
    };

    public TicketValidator()
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Code is required.")
            .Matches(@"^TKT-\d{3}$").WithMessage("Format must be TKT-123.")
            .MustAsync(async (code, ct) =>
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
                return !Used.Contains(code);
            }).WithMessage("Code is already reserved.");
    }
}

// Custom ValidationAttribute showcase. Three flavors flow through Rask's DataAnnotationsValidator
// unchanged because System.ComponentModel.DataAnnotations.Validator walks every attribute on the
// property — there's no opt-in needed for user-authored subclasses:
//   • StrongPassword overrides IsValid(object?) — the simplest shape.
//   • MatchesProperty overrides GetValidationResult(object?, ValidationContext) — uses
//     ValidationContext.ObjectInstance to do cross-field comparison.
//   • NotBanned overrides GetValidationResult and resolves IBannedWordService via
//     ValidationContext.GetService<T>() — proves the render-scoped IServiceProvider flows through.
public sealed class CustomAttributeDemo : Component
{
    private readonly CustomAttributeModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render() =>
        [
            Form<CustomAttributeModel>(
                _model,
                m => _submission = $"Welcome, {m.Username}!",
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                Div()[
                    Label("v12-username", Class: "form-label small mb-1")["Username"],
                    Input(() => _model.Username, Id: "v12-username", Class: "form-control"),
                    ValidationMessage(() => _model.Username, FieldError)
                ],
                Div()[
                    Label("v12-password", Class: "form-label small mb-1")["Password"],
                    Input(() => _model.Password, Id: "v12-password", Type: "password", Class: "form-control"),
                    ValidationMessage(() => _model.Password, FieldError)
                ],
                Div()[
                    Label("v12-confirm", Class: "form-label small mb-1")["Confirm password"],
                    Input(() => _model.ConfirmPassword, Id: "v12-confirm", Type: "password", Class: "form-control"),
                    ValidationMessage(() => _model.ConfirmPassword, FieldError)
                ],
                Div()[
                    Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-shield-check me-1"), "Create account"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]];
}

public sealed class CustomAttributeModel
{
    [Required(ErrorMessage = "Username is required.")]
    [NotBanned(ErrorMessage = "\"{0}\" isn't available.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [StrongPassword(ErrorMessage = "Password must be at least 8 characters and mix letters and digits.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password.")]
    [MatchesProperty(nameof(Password), ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";
}

// Resolved from the form's render-scoped IServiceProvider by [NotBanned]'s GetValidationResult.
public interface IBannedWordService
{
    IReadOnlyCollection<string> Words { get; }
}

public sealed class BannedWordService : IBannedWordService
{
    public IReadOnlyCollection<string> Words { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin", "root", "test" };
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string s || s.Length < 8)
        {
            return false;
        }

        bool hasLetter = false, hasDigit = false;
        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(ch))
            {
                hasDigit = true;
            }

            if (hasLetter && hasDigit)
            {
                return true;
            }
        }

        return false;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MatchesPropertyAttribute(string otherProperty) : ValidationAttribute
{
    public string OtherProperty { get; } = otherProperty;

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification =
            "GetProperty on the model's runtime type — the model is preserved by the user's binding setup, same contract as the validator itself.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var instance = validationContext.ObjectInstance;
        var sibling = instance.GetType().GetProperty(OtherProperty);
        if (sibling is null)
        {
            return new ValidationResult($"Unknown property '{OtherProperty}'.");
        }

        var other = sibling.GetValue(instance);
        return Equals(value, other)
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage ?? $"Must match {OtherProperty}.",
                validationContext.MemberName is null ? null : new[] { validationContext.MemberName });
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBannedAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // No SP, no enforcement — the rule degrades gracefully when the host hasn't registered
        // the service. ASP.NET Core MVC's own attributes behave the same way when GetService
        // returns null. This means tests that bypass the live render path see the attribute
        // pass for any value; the dedicated DI test pushes a LiveRenderContext to opt in.
        var svc = (IBannedWordService?)validationContext.GetService(typeof(IBannedWordService));
        if (svc is null || value is not string s || s.Length == 0)
        {
            return ValidationResult.Success;
        }

        return svc.Words.Contains(s)
            ? new ValidationResult(FormatErrorMessage(s),
                validationContext.MemberName is null ? null : new[] { validationContext.MemberName })
            : ValidationResult.Success;
    }
}
