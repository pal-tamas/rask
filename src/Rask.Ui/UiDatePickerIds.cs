using System.Globalization;

namespace Rask;

/// <summary>
/// Names each date picker's popup. One counter for both pickers: a static in the generic control would be one
/// count per bound type, and a day picker and a range picker on one page would share the name "1".
/// </summary>
internal static class UiDatePickerIds
{
    private static int _instances;

    internal static string Next() =>
        "ui-date-picker-" + Interlocked.Increment(ref _instances).ToString(CultureInfo.InvariantCulture);
}
