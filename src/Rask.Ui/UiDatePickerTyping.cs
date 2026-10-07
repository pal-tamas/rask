using System.Globalization;

namespace Rask;

/// <summary>A typed date, segment by segment: what each field shows, and when three of them make a day.</summary>
internal static class UiDatePickerTyping
{
    /// <summary>What a segment shows: what was typed into it, else its part of the chosen date.</summary>
    internal static string Text(UiDatePickerScope scope, int slot, char part)
    {
        if (scope.Held.TryGetValue((slot, part), out var typed))
        {
            return typed;
        }

        return scope.Dates[slot] is { } date ? Format(date, part) : "";
    }

    /// <summary>Takes what was typed into one segment, and writes the date once all three read as one.</summary>
    internal static Task TypeAsync(UiDatePickerScope scope, int slot, char part, string text)
    {
        scope.Held[(slot, part)] = text.Trim();
        if (!Number(scope, slot, 'y', out var year) || !Number(scope, slot, 'M', out var month) || !Number(scope, slot, 'd', out var day))
        {
            return Task.CompletedTask;
        }

        if (year is < 1000 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return Task.CompletedTask;
        }

        foreach (var held in "Mdy")
        {
            scope.Held.Remove((slot, held));
        }

        return scope.Typed(slot, new DateOnly(year, month, day));
    }

    private static bool Number(UiDatePickerScope scope, int slot, char part, out int number) =>
        int.TryParse(Text(scope, slot, part), NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static string Format(DateOnly date, char part) => part switch
    {
        'M' => date.Month.ToString("00", CultureInfo.InvariantCulture),
        'd' => date.Day.ToString("00", CultureInfo.InvariantCulture),
        _ => date.Year.ToString("0000", CultureInfo.InvariantCulture),
    };
}
