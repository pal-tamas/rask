using System.Globalization;

namespace Rask;

/// <summary>
///     What every <see cref="UiTimePicker{T}" /> shares whatever it is bound to: the class literals measured from
///     Flux's time picker, the list of times and how one is written.
/// </summary>
internal static class UiTimePickerLook
{
    /// <summary>The picker itself: as wide as it is given, and never under 240px.</summary>
    internal const string Root = "block min-w-60";

    /// <summary>The room a clear button is placed in.</summary>
    internal const string RootClearable = "relative block min-w-60";

    /// <summary>The button that shows the choice: white, zinc-200 with a darker bottom edge, <c>shadow-xs</c>.</summary>
    internal const string Button =
        "flex items-center w-full px-3 overflow-hidden cursor-default border shadow-xs disabled:shadow-none "
        + "bg-white dark:bg-white/10 dark:disabled:bg-white/[7%] "
        + "border-zinc-200 border-b-zinc-300/80 disabled:border-b-zinc-200 dark:border-white/10 dark:disabled:border-white/5 "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500";

    /// <summary>The clock before the choice and the chevron after it.</summary>
    internal const string ButtonIcon = "me-2 text-zinc-300 dark:text-white/60";

    internal const string ButtonChevron = "ms-2 -me-1 text-zinc-300 dark:text-white/60";

    /// <summary>What the button says: one line, cut off rather than wrapped.</summary>
    internal const string Selected =
        "flex grow gap-2 overflow-hidden whitespace-nowrap text-start text-zinc-700 dark:text-zinc-300 "
        + "[button:disabled_&]:text-zinc-500 dark:[button:disabled_&]:text-zinc-400";

    internal const string Placeholder = "text-zinc-400";

    /// <summary>The typed trigger: the input's box, holding three fields in a row.</summary>
    internal const string InputBox =
        "flex items-center w-full px-3 cursor-default " + UiInputLook.Outline + " " + UiInputLook.InputShadow;

    internal const string InputIcon = "me-2 text-zinc-400/75 dark:text-white/60";

    internal const string InputChevron = "ms-2 -me-1 text-zinc-400/75 dark:text-white/60";

    /// <summary>The three fields, left to right in every language: a clock reads that way.</summary>
    internal const string Segments = "flex items-center min-w-0 -ms-px overflow-hidden";

    /// <summary>One typed field: two characters wide, centred, with no box of its own.</summary>
    internal const string Segment =
        "block w-[calc(2ch+2px)] p-0 rounded-sm bg-transparent font-mono text-center focus:ring-0 focus:ring-offset-0 "
        + "placeholder:text-zinc-400 disabled:text-zinc-500";

    /// <summary>The list of times, in the top layer under its trigger.</summary>
    internal const string Options =
        "m-0 p-[5px] min-w-48 max-h-80 overflow-auto scroll-py-[5px] rounded-lg border shadow-xs text-inherit "
        + "border-zinc-200 dark:border-zinc-600 bg-white dark:bg-zinc-700";

    /// <summary>
    ///     One time in the list. The cursor's is tinted, and only while the list is open: the markup names a
    ///     cursor before the first press, so a list the browser opens with no round trip already shows one.
    /// </summary>
    internal const string Option =
        "flex items-center justify-start gap-2 w-full px-1 py-1.5 rounded-lg text-base sm:text-sm tabular-nums cursor-default "
        + "text-zinc-800 dark:text-white disabled:text-zinc-400 dark:disabled:text-zinc-400 "
        + "[:popover-open>&]:data-active:bg-zinc-100 dark:[:popover-open>&]:data-active:bg-zinc-600 "
        // The list opens scrolled to its cursor — the chosen time, or OpenTo — brought just into view from the
        // top, as Flux's script does. The platform's own answer, so it needs no round trip and no script.
        + "data-active:snap-end data-active:[scroll-initial-target:nearest]";

    /// <summary>The check mark's column, kept whether or not the time is chosen so the times line up.</summary>
    internal const string Check = "w-6 shrink-0";

    /// <summary>The button that empties a picker, over the trigger's end.</summary>
    internal const string Clear = "absolute top-0 bottom-0 end-9 my-auto " + UiInputLook.Action;

    internal static readonly Dictionary<string, string?> RootMarks = new(StringComparer.Ordinal)
    {
        ["ui-control"] = null,
        ["ui-time-picker"] = null,
    };

    internal static readonly Dictionary<string, string?> ButtonMarks = new(StringComparer.Ordinal)
    {
        ["ui-group-target"] = null,
        ["ui-time-picker-button"] = null,
    };

    internal static readonly Dictionary<string, string?> InvalidButtonMarks = new(ButtonMarks, StringComparer.Ordinal)
    {
        ["invalid"] = null,
    };

    internal static readonly Dictionary<string, string?> BoxMarks = new(StringComparer.Ordinal)
    {
        ["ui-group-target"] = null,
    };

    internal static readonly Dictionary<string, string?> InvalidBoxMarks = new(BoxMarks, StringComparer.Ordinal)
    {
        ["invalid"] = null,
    };

    private static int _instances;

    /// <summary>A number per picker, so two on a page never share a list id.</summary>
    internal static int NextInstance() => Interlocked.Increment(ref _instances);

    /// <summary>Height, text and corners of the button.</summary>
    internal static string ButtonSize(Ui.TimePickerSize size) => size switch
    {
        Ui.TimePickerSize.Sm => "h-8 py-1.5 text-sm rounded-md",
        Ui.TimePickerSize.Xs => "h-6 py-1 text-xs rounded-md",
        _ => "h-10 py-2 text-base sm:text-sm rounded-lg",
    };

    /// <summary>The input's own sizes, for the typed trigger.</summary>
    internal static string BoxSize(Ui.TimePickerSize size) => UiInputLook.Size(size switch
    {
        Ui.TimePickerSize.Sm => Ui.InputSize.Sm,
        Ui.TimePickerSize.Xs => Ui.InputSize.Xs,
        _ => Ui.InputSize.Base,
    });

    /// <summary>Every time on offer: from <paramref name="min" />, every <paramref name="interval" /> minutes, up to <paramref name="max" />.</summary>
    internal static List<TimeOnly> Times(TimeOnly? min, TimeOnly? max, int interval)
    {
        var step = Math.Max(1, interval);
        var last = (int)(max ?? TimeOnly.MaxValue).ToTimeSpan().TotalMinutes;
        var times = new List<TimeOnly>();
        for (var minute = (int)(min ?? TimeOnly.MinValue).ToTimeSpan().TotalMinutes; minute <= last; minute += step)
        {
            times.Add(new TimeOnly(minute / 60, minute % 60));
        }

        return times;
    }

    /// <summary>Whether <paramref name="culture" /> tells the time with AM and PM.</summary>
    internal static bool TwelveHour(Ui.TimePickerTimeFormat format, CultureInfo culture) => format switch
    {
        Ui.TimePickerTimeFormat.TwelveHour => true,
        Ui.TimePickerTimeFormat.TwentyFourHour => false,
        _ => culture.DateTimeFormat.ShortTimePattern.Contains('h', StringComparison.Ordinal),
    };

    /// <summary>A time as the list and the button write it: <c>1:30 PM</c>, <c>13:30</c>, or as the culture does.</summary>
    internal static string Format(TimeOnly time, Ui.TimePickerTimeFormat format, CultureInfo culture)
    {
        var pattern = format switch
        {
            Ui.TimePickerTimeFormat.TwelveHour => "h:mm tt",
            Ui.TimePickerTimeFormat.TwentyFourHour => "HH:mm",
            _ => culture.DateTimeFormat.ShortTimePattern,
        };

        // ICU separates the period with a narrow no-break space; Flux's list has a plain one.
        return time.ToString(pattern, culture).Replace('\u202F', ' ');
    }

    /// <summary>The culture's word for the morning or the afternoon, AM and PM where it has none.</summary>
    internal static string Period(bool afternoon, CultureInfo culture)
    {
        var (word, fallback) = afternoon ? (culture.DateTimeFormat.PMDesignator, "PM") : (culture.DateTimeFormat.AMDesignator, "AM");
        return string.IsNullOrEmpty(word) ? fallback : word;
    }
}
