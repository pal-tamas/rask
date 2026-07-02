using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed class CrossFieldSummaryDemo : Component
{
    private readonly TripModel _model = new();
    private string? _submission;

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        entries.Count == 0
            ? null
            : BsAlert(Color: BsColor.Danger, Class: "small mb-0")[
                Ul(Class: "mb-0 ps-3")[
                    entries.Select((e, i) => Li(Key: i)[
                        e.Field.Length == 0
                            ? e.Message
                            : [Strong()[e.Field], ": ", e.Message]
                    ])
                ]
            ];

    protected override Component? Render() =>
    [
        Form<TripModel>(
            _model,
            OnValidSubmit: m => _submission = $"Booked: {m.Depart:yyyy-MM-dd} → {m.Return:yyyy-MM-dd}",
            Class: "vstack gap-3",
            Validate: m =>
                m.Return > m.Depart
                    ? Array.Empty<string>()
                    : new[] { "Return date must be after departure." })[
            ValidationSummary(SummaryAlert),
            Div()[
                Label("v5-depart", Class: "form-label small mb-1")["Departure"],
                Input(() => _model.Depart, Id: "v5-depart", Class: "form-control")
            ],
            Div()[
                Label("v5-return", Class: "form-label small mb-1")["Return"],
                Input(() => _model.Return, Id: "v5-return", Class: "form-control")
            ],
            Div()[
                BsButton(Type: "submit", Color: BsColor.Primary)[BsIcon(Name: BsIconName.Airplane, Class: "me-1"), "Book"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _submission]
    ];
}

public sealed class TripModel
{
    public DateOnly Depart { get; set; } = new(2026, 6, 1);
    public DateOnly Return { get; set; } = new(2026, 6, 1);
}
