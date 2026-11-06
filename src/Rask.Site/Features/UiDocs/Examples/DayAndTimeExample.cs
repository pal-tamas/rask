using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

// One DateTime? in a form: the day typed or picked, the time beside it, and the time beneath where the row is narrow.
public sealed partial class DayAndTimeExample : Component
{
    private readonly Delivery _delivery = new();
    private string _said = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_delivery).OnSubmit(Save).Id("udt-form").Class("max-w-md space-y-6")[
                Ui.DayAndTime.Bind(() => _delivery.Arrives).Label("Arrives").Id("udt-arrives"),
                Ui.Button.Type(Ui.ButtonType.Submit).Primary["Save"]
            ],
            Ui.Text.Id("udt-said")[_said]
        ];

    private void Save(Delivery delivery) =>
        _said = delivery.Arrives is { } at ? "Saved " + at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "." : "Saved with no time.";

    public sealed class Delivery
    {
        public DateTime? Arrives { get; set; }
    }
}
