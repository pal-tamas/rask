using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarBoundExample : Component
{
    private readonly Appointment _appointment = new();

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Calendar.Bind(() => _appointment.Date).Live().Class("w-fit"),
            Ui.Text["Booked for " + _appointment.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "."]
        ];

    public sealed class Appointment
    {
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);
    }
}
