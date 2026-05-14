using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Rask.Core.Forms;
using static Rask.Validation.DataAnnotations.Components;
using static Rask.Validation.FluentValidation.Components;

namespace Rask.Example.Shared.Demos;

public sealed class ValidationFieldsDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

    protected override Component Render() =>
        Fragment()[
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
                entries.Select(e => (Child)Li()[
                    e.Field.Length == 0
                        ? (Child)e.Message
                        : (Child)Fragment()[Strong()[e.Field], ": ", e.Message]
                ])
            ]
        ];

    protected override Component Render() =>
        Fragment()[
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
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

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
                formOnly.Select(e => (Child)Li()[e.Message])
            ]
        ];
    }

    protected override Component Render() =>
        Fragment()[
            Form(
                Model: _model,
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
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return Fragment();
        }

        return Div(Class: "alert alert-danger small mb-0")[
            Ul(Class: "mb-0 ps-3")[formOnly.Select(e => (Child)Li()[e.Message])]
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

    protected override Component Render() =>
        Fragment()[
            Form<PromoModel>(
                Model: _model,
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
                        ValidatingIndicator(() => _model.Code, "validating-indicator text-muted small mt-1")[
                            I(Class: "bi bi-arrow-clockwise me-1"), "Checking…"
                        ],
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
    private readonly SignupModel _model = new();
    private readonly EditContext _ctx;
    private string? _submission;

    public AsyncValidationDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new UniqueUsernameValidator());
    }

    protected override Component Render() =>
        Fragment()[
            Form<SignupModel>(
                _model,
                m => _submission = $"Signed up: {m.Username}",
                Context: _ctx,
                Class: "vstack gap-3")[
                    DataAnnotationsValidator(),
                    Div()[
                        Label("v3-username", Class: "form-label small mb-1")["Username"],
                        Input(() => _model.Username, Id: "v3-username", Class: "form-control"),
                        ValidatingIndicator(() => _model.Username, "validating-indicator text-muted small mt-1")[
                            I(Class: "bi bi-arrow-clockwise me-1"), "Checking availability..."
                        ],
                        ValidationMessage(() => _model.Username,
                            msgs => Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])])
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
            await CheckAsync(context, new FieldIdentifier(m, nameof(SignupModel.Username)), m.Username, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
    {
        if (context.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
        {
            await CheckAsync(context, field, m.Username, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CheckAsync(EditContext context, FieldIdentifier field, string username, CancellationToken ct)
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
                    entries.Select(e => (Child)Li()[
                        e.Field.Length == 0
                            ? (Child)e.Message
                            : (Child)Fragment()[Strong()[e.Field], ": ", e.Message]
                    ])
                ]
            ];

    protected override Component Render() =>
        Fragment()[
            Form<TripModel>(
                Model: _model,
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
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return Fragment();
        }

        return Div(Class: "alert alert-danger small mb-0")[
            Ul(Class: "mb-0 ps-3")[formOnly.Select(e => (Child)Li()[e.Message])]
        ];
    }

    protected override Component Render() =>
        Fragment()[
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
    private readonly TaskModel _model = new();
    private readonly EditContext _ctx;
    private string? _submission;

    public ProgrammaticValidateDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new SlowTitleValidator());
    }

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

    private async Task ValidateNowAsync()
    {
        await _ctx.ValidateAsync().ConfigureAwait(false);
    }

    protected override Component Render() =>
        Fragment()[
            Form<TaskModel>(
                _model,
                m => _submission = $"Saved task: {m.Title}",
                Context: _ctx,
                Class: "vstack gap-3")[
                    Div()[
                        Label("v6-title", Class: "form-label small mb-1")["Title"],
                        Input(() => _model.Title, Id: "v6-title", Class: "form-control"),
                        ValidatingIndicator(() => _model.Title, "validating-indicator text-muted small mt-1")[
                            I(Class: "bi bi-arrow-clockwise me-1"), "Checking…"
                        ],
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
            await CheckAsync(context, new FieldIdentifier(m, nameof(TaskModel.Title)), m.Title, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
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
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

    protected override Component Render() =>
        Fragment()[
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
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

    protected override Component Render() =>
        Fragment()[
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
        Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small mt-1")[m])];

    protected override Component Render() =>
        Fragment()[
            Form<TicketModel>(
                _model,
                m => _submission = $"Reserved: {m.Code}",
                Class: "vstack gap-3")[
                    FluentValidationValidator(new TicketValidator()),
                    Div()[
                        Label("v9-code", Class: "form-label small mb-1")["Ticket code"],
                        Input(() => _model.Code, Id: "v9-code", Class: "form-control"),
                        ValidatingIndicator(() => _model.Code, "validating-indicator text-muted small mt-1")[
                            I(Class: "bi bi-arrow-clockwise me-1"), "Checking availability..."
                        ],
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
