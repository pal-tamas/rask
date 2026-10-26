namespace Rask;

/// <summary>
///     The datetime picker's steps that take what Flux's <c>unavailable</c> takes besides its slots: moments, each a
///     single slot — <c>.Unavailable(bookings.Select(b =&gt; b.StartsAt))</c>, Flux's array of <c>Carbon</c> instances.
/// </summary>
/// <remarks>Each hands its value to the generated step, which is what marks the prop written for this render.</remarks>
public static class UiDatetimePickerSteps
{
    /// <summary>Moments that cannot be picked, each one minute on its day.</summary>
    /// <param name="picker">The picker.</param>
    /// <param name="moments">The moments: existing bookings' starts.</param>
    /// <returns><paramref name="picker" />.</returns>
    public static UiDatetimePicker Unavailable(this UiDatetimePicker picker, IEnumerable<DateTime> moments) =>
        picker.Unavailable(Slots(moments));

    /// <inheritdoc cref="Unavailable(UiDatetimePicker, IEnumerable{DateTime})" />
    public static UiDatetimePickerRange Unavailable(this UiDatetimePickerRange picker, IEnumerable<DateTime> moments) =>
        picker.Unavailable(Slots(moments));

    private static List<UiDatetimeSlot> Slots(IEnumerable<DateTime> moments)
    {
        ArgumentNullException.ThrowIfNull(moments);
        return [.. moments.Select(UiDatetimeSlot.FromDateTime)];
    }
}
