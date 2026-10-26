using System.Globalization;
using static Rask.Markup;

namespace Rask;

/// <summary>
///     What a time picker's typed trigger and a datetime picker's typed time share: the hour, minute and AM/PM
///     parts as the runtime's <c>data-rask-segments</c> reads them, and a row of the list of times.
/// </summary>
internal static class UiTimeParts
{
    /// <summary>
    ///     Hour, minute and — on a twelve-hour clock — AM/PM, typed as ONE field: the runtime takes the digits, moves on
    ///     from a part that can take no more, steps a part on the vertical arrows and sets AM or PM on <c>a</c> and
    ///     <c>p</c>, as Flux's fields do.
    /// </summary>
    /// <param name="twelve">Whether the clock has AM and PM.</param>
    /// <param name="culture">Whose words AM and PM are.</param>
    /// <param name="disabled">Whether the parts take no input.</param>
    /// <param name="hour">What the caller adds to the hour, which a label points at.</param>
    /// <param name="look">The hour's and minute's class: the time picker's own unless given.</param>
    /// <param name="period">The AM/PM part's class: <paramref name="look" /> unless given.</param>
    /// <param name="space">What stands between the minute and AM/PM: a space a flex row lets fall, unless given.</param>
    internal static IEnumerable<Component> Of(
        bool twelve, CultureInfo culture, bool disabled, Func<HTMLInputElement<string>, HTMLInputElement<string>>? hour = null,
        string look = UiTimePickerLook.Segment, string? period = null, string space = " ")
    {
        var first = Part("hour", RaskStrings.Get(RaskString.PickerHour, "Hour"), RaskStrings.Get(RaskString.TimePickerHourPlaceholder, "hh"), disabled, look)
            .Attributes(UiTimePickerLook.Numeric);
        yield return hour is null ? first : hour(first);
        yield return Markup.Text.Value(":");
        yield return Part("minute", RaskStrings.Get(RaskString.PickerMinute, "Minute"), RaskStrings.Get(RaskString.TimePickerMinutePlaceholder, "mm"), disabled, look)
            .Attributes(UiTimePickerLook.Numeric);
        if (twelve)
        {
            yield return Markup.Text.Value(space);
            yield return Part("meridiem", RaskStrings.Get(RaskString.TimePickerMeridiem, "AM/PM"), UiTimePickerLook.Period(false, culture), disabled, period ?? look);
        }
    }

    /// <summary>
    ///     One time of a list, with no handler: the check mark's column and the time. The caller adds what a press
    ///     does — a page's handler, or nothing where the runtime hears it.
    /// </summary>
    /// <param name="listId">The list's id, which the row's own is made from.</param>
    /// <param name="index">The row's place in the list.</param>
    /// <param name="time">The time it stands for.</param>
    /// <param name="selected">Whether it is chosen.</param>
    /// <param name="start">Whether the list opens on it.</param>
    /// <param name="off">Whether it cannot be picked.</param>
    internal static HTMLButtonElement Row(string listId, int index, TimeOnly time, bool selected, bool start, bool off)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["time"] = time.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
        if (selected)
        {
            data["selected"] = null;
        }

        if (start)
        {
            // Where the list opens, lit: the runtime lights it again at every opening, and brings it into view.
            data["active"] = null;
            data["rask-time-start"] = null;
        }

        return Button
            .Key(index)
            .Id(UiSelectNav.OptId(listId, index))
            .Type(ButtonType.Button)
            .Role("option")
            .TabIndex(-1)
            .Class(UiTimePickerLook.Option)
            .Disabled(off)
            .Data(data)
            .Aria("selected", selected ? "true" : "false");
    }

    /// <summary>What a row of the list holds: the check mark's column, kept so the times line up, and the time.</summary>
    internal static Component[] RowContent(string text, bool selected) =>
    [
        Div.Class(UiTimePickerLook.Check).Data("checked", null)[
            Ui.Icon.Name(Ui.IconName.Check).Mini.Class(selected ? null : "hidden")
        ],
        Div.Attributes(("dir", "ltr"))[text]
    ];

    private static HTMLInputElement<string> Part(string kind, string label, string placeholder, bool disabled, string look) =>
        Input.Of<string>()
            .Key(kind)
            .Type(InputType.Text)
            .Placeholder(placeholder)
            .Disabled(disabled)
            .Class(look)
            .Data(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-" + kind + "-input"] = null, ["rask-segment"] = kind })
            .Aria("label", label);
}
