using System.Globalization;

namespace Rask;

/// <summary>Names each datetime picker's popup. One counter for both pickers: a static in the generic control is one per bound type.</summary>
internal static class UiDatetimePickerIds
{
    private static int _instances;

    internal static string Next() =>
        "ui-datetime-picker-" + Interlocked.Increment(ref _instances).ToString(CultureInfo.InvariantCulture);
}
