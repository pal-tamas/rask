namespace Rask.Example.Shared.Features;

// The custom-popover date/time pickers, bound to a model. BsDatePicker/BsTimePicker/BsDateTimePicker
// implement IFormControl<T>, so two-way binding is automatic — and the readout OUTSIDE the Form updates
// on every change with no StateHasChanged, because a bound write re-renders the expression's owner. Each
// box is a hand-editable <input>: type a date/time (parsed live in the current culture) OR focus it to open
// the calendar/clock popover (pure live-diff view state, zero bootstrap.js). A nullable DateOnly? gets a
// clear (×) button.
public sealed class BsPickersDemo : Component
{
    private readonly Booking _model = new()
    {
        Day = DateOnly.FromDateTime(DateTime.Today),
        Time = new TimeOnly(9, 0),
        When = DateTime.Today.AddHours(9),
    };

    protected override Component? Render() =>
    [
        Form<Booking>(_model, Class: "vstack gap-3")[
            BsDatePicker(() => _model.Day, Label: "Date", Id: "pick-date"),
            BsTimePicker(() => _model.Time, Label: "Time", MinuteStep: 15, Id: "pick-time"),
            BsDateTimePicker(() => _model.When, Label: "Date & time", Id: "pick-datetime"),
            BsDatePicker<DateOnly?>(() => _model.Deadline, Label: "Deadline (optional, clearable)", Id: "pick-deadline")
        ],
        BsAlert(Color: BsColor.Info, Class: "mt-3 mb-0")[
            Span(Id: "pick-readout")[
                $"{_model.Day:yyyy-MM-dd} · {_model.Time:HH\\:mm} · {_model.When:yyyy-MM-dd HH\\:mm}" +
                $" · {(_model.Deadline is { } dl ? dl.ToString("yyyy-MM-dd") : "—")}"
            ]
        ]
    ];

    private sealed class Booking
    {
        public DateOnly Day { get; set; }

        public TimeOnly Time { get; set; }

        public DateTime When { get; set; }

        public DateOnly? Deadline { get; set; }
    }
}
