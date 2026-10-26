namespace Rask;

/// <summary>How wide a date or a time picker is, measured from Flux UI's.</summary>
/// <remarks>
///     Flux's pickers are never narrower than 12rem, 15rem from <c>sm</c> up — until the call site sizes them:
///     with a class of its own (<c>flex-1</c>, <c>w-40</c>) the minimum is gone, so the class alone decides.
/// </remarks>
internal static class UiPickerWidth
{
    private const string Least = "min-w-[12rem] sm:min-w-[240px]";

    private const string Sized = "[:where(&)]:min-w-0";

    /// <summary>The root's width classes for a picker given <paramref name="call" /> by its call site.</summary>
    internal static string For(string? call) => string.IsNullOrWhiteSpace(call) ? Least : Sized;
}
