using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed class ValidationFieldsDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

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
                Input(() => _model.Email, Id: "v1-email", Type: InputType.Email,
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
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];
}
