using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using static Rask.Validation.DataAnnotations.Components;

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
            Form<LoginModel>(
                _model,
                m => _submission = $"Welcome, {m.Email}",
                Class: "vstack gap-3",
                Validate: (Func<LoginModel, IEnumerable<string>>)(m =>
                    m.Password == m.Confirm ? Array.Empty<string>() : new[] { "Passwords do not match." }))[
                    Div()[
                        Label("v4-email", Class: "form-label small mb-1")["Email"],
                        Input(() => _model.Email, Id: "v4-email", Type: "email", Class: "form-control",
                            Validate: (Func<string, IEnumerable<string>>)(v =>
                                string.IsNullOrWhiteSpace(v) || v.Contains('@')
                                    ? Array.Empty<string>()
                                    : new[] { "Email looks wrong." })),
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
