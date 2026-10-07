namespace Rask;

/// <summary>What a <see cref="UiOtp" /> tells the cells inside it.</summary>
/// <param name="Code">The characters entered so far.</param>
/// <param name="Total">How many cells there are.</param>
/// <param name="Mode">Which characters a cell takes.</param>
/// <param name="Private">Whether the cells mask what is typed.</param>
/// <param name="Disabled">Whether the cells take input at all.</param>
/// <param name="Invalid">Whether the cells draw the error state.</param>
/// <param name="Autocomplete">The first cell's <c>autocomplete</c>; the others are <c>off</c>.</param>
/// <param name="FirstId">The id the first cell carries, for the field's label.</param>
/// <param name="Redrawn">How often each cell has been replaced by a new element, by cell.</param>
/// <param name="Typed">Reports one cell's text as it is typed.</param>
/// <param name="Settled">Reports one cell's text when focus leaves it.</param>
internal sealed record UiOtpScope(
    string Code,
    int Total,
    Ui.OtpMode Mode,
    bool Private,
    bool Disabled,
    bool Invalid,
    string Autocomplete,
    string FirstId,
    IReadOnlyDictionary<int, int> Redrawn,
    Func<int, string, Task> Typed,
    Action<int, string> Settled)
{
    /// <summary>The one cell Tab stops at: the first empty one, or the last of a full code.</summary>
    internal int Stop => Math.Min(Code.Length, Total - 1);
}
