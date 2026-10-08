namespace Rask;

public static partial class Ui
{
    /// <summary>The clock a <see cref="UiTimePicker{T}" /> writes its times in.</summary>
    public enum TimePickerTimeFormat
    {
        /// <summary>Whichever the culture uses, Flux's default.</summary>
        Auto = 0,

        /// <summary>Flux's <c>12-hour</c>: <c>1:30 PM</c>.</summary>
        TwelveHour,

        /// <summary>Flux's <c>24-hour</c>: <c>13:30</c>.</summary>
        TwentyFourHour,
    }
}
