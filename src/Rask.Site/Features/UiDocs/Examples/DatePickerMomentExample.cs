using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

// Bind decides by the member's type: a DateTime keeps its time of day on the day picked, and a value object is made with
// its own Create — no day after today here, so the calendar offers none.
public sealed partial class DatePickerMomentExample : Component
{
    private readonly Delivery _delivery = new();

    protected override Component? Render() =>
        Form.Model(_delivery).Class("max-w-xs space-y-4")[
            Ui.DatePicker.Bind(() => _delivery.Due).Live().Label("Due").Id("ui-date-picker-due"),
            Ui.DatePicker.Bind(() => _delivery.Invoiced).Live().Clearable().Label("Invoiced").Id("ui-date-picker-invoiced"),
            Ui.Text.Data("testid", "ui-date-picker-moment-state")[
                "Due " + _delivery.Due.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ", "
                + (_delivery.Invoiced is { } day ? "invoiced " + day : "not invoiced") + "."
            ]
        ];

    public sealed class Delivery
    {
        public DateTime Due { get; set; } = new(2026, 11, 6, 14, 30, 0, DateTimeKind.Unspecified);

        public InvoiceDay? Invoiced { get; set; }
    }

    /// <summary>The day a delivery was invoiced: never after today.</summary>
    public sealed partial record InvoiceDay(DateOnly Value) : Rask.Values.Day(Value, NotAfterToday: true);
}
