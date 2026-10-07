using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class CrossFieldSummaryDemo : Component
{
    private readonly TripModel _model = new();
    private string? _submission;

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        entries.Count == 0
            ? null
            : Ui.Callout.Danger.Role("alert")[Ui.CalloutText[Ul.Class("mb-0 ps-3")[
                    entries.Select((e, i) => Li.Key(i)[
                        e.Field.Length == 0
                            ? e.Message
                            : [Strong[e.Field], ": ", e.Message]
                    ])
                ]]];

    protected override Component? Render() =>
    [
        Form.Model(_model)
            .OnSubmit(m => _submission = $"Booked: {m.Depart:yyyy-MM-dd} → {m.Return:yyyy-MM-dd}")
            .Class("flex flex-col gap-3")
            .Validate(m =>
                m.Return > m.Depart
                    ? []
                    : ["Return date must be after departure."])[
            Validation.Summary.Template(SummaryAlert),
            Div[
                Ui.Input.Bind(() => _model.Depart).Label("Departure").Id("v5-depart")
            ],
            Div[
                Ui.Input.Bind(() => _model.Return).Label("Return").Id("v5-return")
            ],
            Div[
                Ui.Button.Primary.Submit[Ui.Icon.Name(Ui.IconName.PaperAirplane), "Book"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_submission)
    ];
}
