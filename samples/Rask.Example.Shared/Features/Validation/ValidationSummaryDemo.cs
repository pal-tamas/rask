using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class ValidationSummaryDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        Div.Class($"{Tw.AlertDanger} text-sm mb-0")[
            Div.Class("font-semibold mb-1")[
                UiIcon.Name(UiIconName.Warning).Class("me-1"),
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
        Form.Model(_model).OnValidSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("flex flex-col gap-3")[
            ValidationSummary.Template(SummaryAlert),
            Div[
                Label.For("v2-name").Class($"{Tw.Label} text-sm mb-1")["Name"],
                Input.Bind(() => _model.Name).Id("v2-name").Class(Tw.Input)
            ],
            Div[
                Label.For("v2-email").Class($"{Tw.Label} text-sm mb-1")["Email"],
                Input.Bind(() => _model.Email)
                    .Id("v2-email")
                    .Type(InputType.Email)
                    .Class(Tw.Input)
            ],
            Div[
                Label.For("v2-age").Class($"{Tw.Label} text-sm mb-1")["Age"],
                Input.Bind(() => _model.Age).Id("v2-age").Class(Tw.Input)
            ],
            Div[
                Label.For("v2-plan").Class($"{Tw.Label} text-sm mb-1")["Plan"],
                Select.Bind(() => _model.Plan).Id("v2-plan").Class(Tw.Select)[
                    Option.Value("")["— choose —"],
                    Option.Value("free")["Free"],
                    Option.Value("pro")["Pro"],
                    Option.Value("team")["Team"]
                ]
            ],
            Div[
                Button.Class(Tw.BtnPrimary).Type("submit")[UiIcon.Name(UiIconName.CheckCircle).Class("me-1"), "Register"]
            ]
        ],
        _submission is null
            ? null
            : Div.Role("status").Class($"{Tw.AlertSuccess} text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle).Class("me-2"), _submission]
    ];
}
