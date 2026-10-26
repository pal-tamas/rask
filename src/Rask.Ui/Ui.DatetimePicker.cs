namespace Rask;

public static partial class Ui
{
    /// <summary>What a datetime picker picks — Flux UI's <c>mode</c>.</summary>
    public enum DatetimePickerMode
    {
        /// <summary>One date and time: a <c>DateTime</c>.</summary>
#pragma warning disable CA1720 // Flux's own name for the mode
        Single = 0,
#pragma warning restore CA1720

        /// <summary>A start and an end, each with its own time: a <see cref="UiDateTimeRange" />.</summary>
        Range,
    }

    /// <summary>How large a datetime picker's trigger and day cells are — Flux UI's <c>size</c>.</summary>
    public enum DatetimePickerSize
    {
        /// <summary>A 40px trigger and 40px day cells.</summary>
        Default = 0,

        /// <summary>A 24px trigger.</summary>
        Xs,

        /// <summary>A 32px trigger and 36px day cells.</summary>
        Sm,

        /// <summary>44px day cells.</summary>
        Lg,

        /// <summary>48px day cells.</summary>
        Xl,

        /// <summary>56px day cells — Flux's <c>2xl</c>.</summary>
        Xxl,
    }

    /// <summary>How a datetime picker's times are chosen — Flux UI's <c>time</c>.</summary>
    public enum DatetimePickerTime
    {
        /// <summary>From a list, every <c>Interval</c> minutes.</summary>
        List = 0,

        /// <summary>Typed into hour, minute and AM/PM fields, with the list as suggestions.</summary>
        Input,
    }

    /// <summary>The clock a datetime picker writes its times in — Flux UI's <c>time-format</c>.</summary>
    public enum DatetimePickerTimeFormat
    {
        /// <summary>Whichever the culture uses, Flux's default.</summary>
        Auto = 0,

        /// <summary>Flux's <c>12-hour</c>: <c>1:30 PM</c>.</summary>
        TwelveHour,

        /// <summary>Flux's <c>24-hour</c>: <c>13:30</c>.</summary>
        TwentyFourHour,
    }

    /// <summary>How tall a datetime picker's button is — <c>flux:datetime-picker.button</c>'s <c>size</c>.</summary>
    public enum DatetimePickerButtonSize
    {
        /// <summary>40px.</summary>
        Base = 0,

        /// <summary>32px.</summary>
        Sm,

        /// <summary>24px.</summary>
        Xs,
    }
}
