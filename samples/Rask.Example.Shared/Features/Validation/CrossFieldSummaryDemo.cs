using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class CrossFieldSummaryDemo : Component
{
    private readonly TripModel _model = new();
    private string? _submission;

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        entries.Count == 0
            ? null
            : Div.Class($"{Tw.AlertDanger} text-sm mb-0")[
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
        Form.Model(_model)
            .OnValidSubmit(m => _submission = $"Booked: {m.Depart:yyyy-MM-dd} → {m.Return:yyyy-MM-dd}")
            .Class("flex flex-col gap-3")
            .Validate(m =>
                m.Return > m.Depart
                    ? Array.Empty<string>()
                    : new[] { "Return date must be after departure." })[
            ValidationSummary.Template(SummaryAlert),
            Div[
                Label.For("v5-depart").Class($"{Tw.Label} text-sm mb-1")["Departure"],
                Input.Bind(() => _model.Depart).Id("v5-depart").Class(Tw.Input)
            ],
            Div[
                Label.For("v5-return").Class($"{Tw.Label} text-sm mb-1")["Return"],
                Input.Bind(() => _model.Return).Id("v5-return").Class(Tw.Input)
            ],
            Div[
                Button.Class(Tw.BtnPrimary).Type("submit")[UiIcon.Name(UiIconName.PaperAirplane).Class("me-1"), "Book"]
            ]
        ],
        _submission is null
            ? null
            : Div.Role("status").Class($"{Tw.AlertSuccess} text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle).Class("me-2"), _submission]
    ];
}

public sealed class TripModel
{
    public DateOnly Depart { get; set; } = new(2026, 6, 1);
    public DateOnly Return { get; set; } = new(2026, 6, 1);
}
