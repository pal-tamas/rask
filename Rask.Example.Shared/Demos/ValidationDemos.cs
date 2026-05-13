using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Demos;

public sealed class ValidationFieldsDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    protected override Component Render() =>
        Fragment()[
            Form<RegistrationModel>(
                _model,
                m => _submission = $"Registered: {m.Name} <{m.Email}>",
                Class: "vstack gap-3")[
                    Div()[
                        Label("v1-name", Class: "form-label small mb-1")["Name"],
                        Input(() => _model.Name, Id: "v1-name", Class: "form-control"),
                        ValidationMessage(() => _model.Name, "text-danger small mt-1")
                    ],
                    Div()[
                        Label("v1-email", Class: "form-label small mb-1")["Email"],
                        Input(() => _model.Email, Id: "v1-email", Type: "email",
                            Class: "form-control"),
                        ValidationMessage(() => _model.Email, "text-danger small mt-1")
                    ],
                    Div()[
                        Label("v1-age", Class: "form-label small mb-1")["Age"],
                        Input(() => _model.Age, Id: "v1-age", Class: "form-control"),
                        ValidationMessage(() => _model.Age, "text-danger small mt-1")
                    ],
                    Div()[
                        Label("v1-plan", Class: "form-label small mb-1")["Plan"],
                        Select(() => _model.Plan, Id: "v1-plan", Class: "form-select")[
                                Option("")["— choose —"],
                                Option("free")["Free"],
                                Option("pro")["Pro"],
                                Option("team")["Team"]
                            ],
                        ValidationMessage(() => _model.Plan, "text-danger small mt-1")
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

    protected override Component Render() =>
        Fragment()[
            Form<RegistrationModel>(
                _model,
                m => _submission = $"Registered: {m.Name} <{m.Email}>",
                Class: "vstack gap-3")[
                    ValidationSummary("alert alert-danger small mb-0"),
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

public sealed class AsyncValidationDemo : Component
{
    private readonly SignupModel _model = new();
    private readonly EditContext _ctx;
    private string? _submission;

    public AsyncValidationDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new DataAnnotationsValidator());
        _ctx.AddValidator(new UniqueUsernameValidator());
    }

    protected override Component Render() =>
        Fragment()[
            Form<SignupModel>(
                _model,
                m => _submission = $"Signed up: {m.Username}",
                Context: _ctx,
                Class: "vstack gap-3")[
                    Div()[
                        Label("v3-username", Class: "form-label small mb-1")["Username"],
                        Input(() => _model.Username, Id: "v3-username", Class: "form-control"),
                        ValidatingIndicator(() => _model.Username, "text-muted small mt-1")[
                            I(Class: "bi bi-arrow-clockwise me-1"), "Checking availability..."
                        ],
                        ValidationMessage(() => _model.Username, "text-danger small mt-1")
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
