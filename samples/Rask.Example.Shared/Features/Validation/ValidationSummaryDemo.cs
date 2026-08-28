using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class ValidationSummaryDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        Div.Class($"{Ui.AlertDanger} small mb-0")[
            Div.Class("fw-semibold mb-1")[
                Icon.Name(IconName.ExclamationTriangle).Class("me-1"),
                $"Please fix {entries.Count} error{(entries.Count == 1 ? "" : "s")}:"
            ],
            Ul.Class("mb-0 ps-3")[
                entries.Select((e, i) => Li.Key(i)[
                    e.Field.Length == 0
                        ? e.Message
                        : [Strong[e.Field], ": ", e.Message]
                ])
            ]
        ];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("vstack gap-3")[
            DataAnnotationsValidator,
            ValidationSummary.Template(SummaryAlert),
            Div[
                Label.For("v2-name").Class("form-label small mb-1")["Name"],
                Input.Bind(() => _model.Name).Id("v2-name").Class("form-control")
            ],
            Div[
                Label.For("v2-email").Class("form-label small mb-1")["Email"],
                Input.Bind(() => _model.Email)
                    .Id("v2-email")
                    .Type(InputType.Email)
                    .Class("form-control")
            ],
            Div[
                Label.For("v2-age").Class("form-label small mb-1")["Age"],
                Input.Bind(() => _model.Age).Id("v2-age").Class("form-control")
            ],
            Div[
                Label.For("v2-plan").Class("form-label small mb-1")["Plan"],
                Select.Bind(() => _model.Plan).Id("v2-plan").Class("form-select")[
                    Option.Value("")["— choose —"],
                    Option.Value("free")["Free"],
                    Option.Value("pro")["Pro"],
                    Option.Value("team")["Team"]
                ]
            ],
            Div[
                Button.Class(Ui.BtnPrimary).Type("submit")[Icon.Name(IconName.Check2Circle).Class("me-1"), "Register"]
            ]
        ],
        _submission is null
            ? null
            : Div.Class($"{Ui.AlertSuccess} small mt-3 mb-0")[Icon.Name(IconName.CheckCircle).Class("me-2"), _submission]
    ];
}
