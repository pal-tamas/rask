using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class ValidationFieldsDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger small mt-1")[m])];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("vstack gap-3")[
            DataAnnotationsValidator,
            Div[
                Label.For("v1-name").Class("form-label small mb-1")["Name"],
                Input.Bind(() => _model.Name).Id("v1-name").Class("form-control"),
                ValidationMessage.Template(FieldError).For(() => _model.Name)
            ],
            Div[
                Label.For("v1-email").Class("form-label small mb-1")["Email"],
                Input.Bind(() => _model.Email)
                    .Id("v1-email")
                    .Type(InputType.Email)
                    .Class("form-control"),
                ValidationMessage.Template(FieldError).For(() => _model.Email)
            ],
            Div[
                Label.For("v1-age").Class("form-label small mb-1")["Age"],
                Input.Bind(() => _model.Age).Id("v1-age").Class("form-control"),
                ValidationMessage.Template(FieldError).For(() => _model.Age)
            ],
            Div[
                Label.For("v1-plan").Class("form-label small mb-1")["Plan"],
                Select.Bind(() => _model.Plan).Id("v1-plan").Class("form-select")[
                    Option.Value("")["— choose —"],
                    Option.Value("free")["Free"],
                    Option.Value("pro")["Pro"],
                    Option.Value("team")["Team"]
                ],
                ValidationMessage.Template(FieldError).For(() => _model.Plan)
            ],
            Div[
                BsButton.Type("submit").Color(BsColor.Primary)[BsIcon.Name(BsIconName.Check2Circle).Class("me-1"), "Register"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert.Color(BsColor.Success).Class("small mt-3 mb-0")[BsIcon.Name(BsIconName.CheckCircle).Class("me-2"), _submission]
    ];
}
