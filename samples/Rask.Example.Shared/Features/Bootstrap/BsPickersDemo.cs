namespace Rask.Example.Shared.Features;

// Every variant of the custom-popover date/time pickers, bound to one model. BsDatePicker/BsTimePicker/
// BsDateTimePicker implement IFormControl<T>, so two-way binding is automatic — and the readout OUTSIDE the
// Form updates on every change with no StateHasChanged, because a bound write re-renders the expression's
// owner. Each box is hand-editable: type a date/time (parsed live in the current culture) OR focus it to open
// the calendar/clock popover (pure live-diff view state, zero bootstrap.js). Floating wraps the label like a
// form-floating; Native drops to the OS <input>; Min/Max/Disable constrain the calendar; a nullable T gets an
// × clear button.
public sealed class BsPickersDemo : Component
{
    private readonly Booking _model = new()
    {
        Day = DateOnly.FromDateTime(DateTime.Today),
        Time = new TimeOnly(9, 0),
        When = DateTime.Today.AddHours(9),
        DayFloat = DateOnly.FromDateTime(DateTime.Today),
        WhenFloat = DateTime.Today.AddHours(9),
    };

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    protected override Component? Render() =>
    [
        Form<Booking>(_model, Class: "vstack gap-3")[
            // Date — default, floating, native, and range-constrained (next 30 days, no weekends).
            BsDatePicker(() => _model.Day, Label: "Date", Id: "pick-date"),
            BsDatePicker(() => _model.DayFloat, Label: "Date (floating)", Floating: true, Id: "pick-date-float"),
            BsDatePicker(() => _model.DayNative, Label: "Date (native)", Native: true, Id: "pick-date-native"),
            BsDatePicker(() => _model.DayRange, Label: "Date (next 30 days, weekdays only)",
                Min: Today, Max: Today.AddDays(30),
                Disable: d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday, Id: "pick-date-range"),
            BsDatePicker<DateOnly?>(() => _model.Deadline, Label: "Deadline (nullable, clearable)", Id: "pick-deadline"),

            // Time — stepped, floating, and native.
            BsTimePicker(() => _model.Time, Label: "Time (15-min steps)", MinuteStep: 15, Id: "pick-time"),
            BsTimePicker(() => _model.TimeNative, Label: "Time (native)", Native: true, Id: "pick-time-native"),

            // Date & time — default, floating, and native.
            BsDateTimePicker(() => _model.When, Label: "Date & time", Id: "pick-datetime"),
            BsDateTimePicker(() => _model.WhenFloat, Label: "Date & time (floating)", Floating: true, Id: "pick-datetime-float"),
            BsDateTimePicker(() => _model.WhenNative, Label: "Date & time (native)", Native: true, Id: "pick-datetime-native")
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
        public DateOnly DayFloat { get; set; }
        public DateOnly DayNative { get; set; }
        public DateOnly DayRange { get; set; }
        public DateOnly? Deadline { get; set; }

        public TimeOnly Time { get; set; }
        public TimeOnly TimeNative { get; set; }

        public DateTime When { get; set; }
        public DateTime WhenFloat { get; set; }
        public DateTime WhenNative { get; set; }
    }
}
