using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed class InlineValidateDemo : Component
{
    private readonly LoginModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        // Filter to form-level entries — per-field rules already render through FieldError.
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return Fragment();
        }

        return BsAlert(Color: BsColor.Danger, Class: "small mb-0")[
            Ul(Class: "mb-0 ps-3")[
                formOnly.Select((e, i) => Li(Key: i)[e.Message])
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
                Input(() => _model.Email, Id: "v4-email", Type: InputType.Email, Class: "form-control",
                    Validate: v =>
                        v.Contains('@')
                            ? Array.Empty<string>()
                            : new[] { "Email looks wrong." }),
                ValidationMessage(() => _model.Email, FieldError)
            ],
            Div()[
                Label("v4-password", Class: "form-label small mb-1")["Password"],
                Input(() => _model.Password, Id: "v4-password", Type: InputType.Password, Class: "form-control")
            ],
            Div()[
                Label("v4-confirm", Class: "form-label small mb-1")["Confirm"],
                Input(() => _model.Confirm, Id: "v4-confirm", Type: InputType.Password, Class: "form-control")
            ],
            ValidationSummary(SummaryAlert),
            Div()[
                Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Sign in"]
            ]
        ],
        _submission is null
            ? Fragment()
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];
}

public sealed class LoginModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Confirm { get; set; } = "";
}
