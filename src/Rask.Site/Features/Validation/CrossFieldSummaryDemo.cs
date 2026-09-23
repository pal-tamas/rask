using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class CrossFieldSummaryDemo : Component
{
    private readonly TripModel _model = new();
    private string? _submission;

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        entries.Count == 0
            ? null
            : Ui.Alert.Tone(Ui.Tone.Error).Variant(Ui.Variant.Soft).Class("text-sm mb-0")[Ul.Class("mb-0 ps-3")[
                    entries.Select((e, i) => Li.Key(i)[
                        e.Field.Length == 0
                            ? e.Message
                            : [Strong[e.Field], ": ", e.Message]
                    ])
                ]];

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
                Ui.Input.Bind(() => _model.Depart).Label("Departure").Id("v5-depart")
            ],
            Div[
                Ui.Input.Bind(() => _model.Return).Label("Return").Id("v5-return")
            ],
            Div[
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.PaperAirplane), "Book"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}

public sealed class TripModel
{
    public DateOnly Depart { get; set; } = new(2026, 6, 1);
    public DateOnly Return { get; set; } = new(2026, 6, 1);
}
