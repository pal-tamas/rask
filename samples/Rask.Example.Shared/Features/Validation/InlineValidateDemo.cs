using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class InlineValidateDemo : Component
{
    private readonly LoginModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger small mt-1")[m])];

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        // Filter to form-level entries — per-field rules already render through FieldError.
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return null;
        }

        return BsAlert.Color(BsColor.Danger).Class("small mb-0")[
            Ul.Class("mb-0 ps-3")[
                formOnly.Select((e, i) => Li.Key(i)[e.Message])
            ]
        ];
    }

    protected override Component? Render() =>
    [
        Form.Model(_model)
            .OnValidSubmit(m => _submission = $"Welcome, {m.Email}")
            .Class("vstack gap-3")
            .Validate(m =>
                m.Password == m.Confirm ? Array.Empty<string>() : new[] { "Passwords do not match." })[
            Div[
                Label.For("v4-email").Class("form-label small mb-1")["Email"],
                Input.Bind(() => _model.Email)
                    .Id("v4-email")
                    .Type(InputType.Email)
                    .Class("form-control")
                    .Validate(v =>
                        v.Contains('@')
                            ? Array.Empty<string>()
                            : new[] { "Email looks wrong." }),
                ValidationMessage.Template(FieldError).For(() => _model.Email)
            ],
            Div[
                Label.For("v4-password").Class("form-label small mb-1")["Password"],
                Input.Bind(() => _model.Password).Id("v4-password").Type(InputType.Password).Class("form-control")
            ],
            Div[
                Label.For("v4-confirm").Class("form-label small mb-1")["Confirm"],
                Input.Bind(() => _model.Confirm).Id("v4-confirm").Type(InputType.Password).Class("form-control")
            ],
            ValidationSummary.Template(SummaryAlert),
            Div[
                BsButton.Type("submit").Color(BsColor.Primary)[BsIcon.Name(BsIconName.Check2Circle).Class("me-1"), "Sign in"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert.Color(BsColor.Success).Class("small mt-3 mb-0")[BsIcon.Name(BsIconName.CheckCircle).Class("me-2"), _submission]
    ];
}

public sealed class LoginModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Confirm { get; set; } = "";
}
