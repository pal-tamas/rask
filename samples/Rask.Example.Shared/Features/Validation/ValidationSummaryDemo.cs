using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed class ValidationSummaryDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        BsAlert(Color: BsColor.Danger, Class: "small mb-0")[
            Div(Class: "fw-semibold mb-1")[
                I(Class: "bi bi-exclamation-triangle me-1"),
                $"Please fix {entries.Count} error{(entries.Count == 1 ? "" : "s")}:"
            ],
            Ul(Class: "mb-0 ps-3")[
                entries.Select((e, i) => Li(Key: i)[
                    e.Field.Length == 0
                        ? e.Message
                        : Fragment()[Strong()[e.Field], ": ", e.Message]
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
                Input(() => _model.Email, Id: "v2-email", Type: InputType.Email,
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
                BsButton(Type: "submit", Color: BsColor.Primary)[I(Class: "bi bi-check2-circle me-1"), "Register"]
            ]
        ],
        _submission is null
            ? Fragment()
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];
}
