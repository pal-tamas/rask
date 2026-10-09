namespace Rask;

/// <summary>What a <see cref="UiOtp" /> tells the cells inside it.</summary>
/// <param name="Entered">How many characters the code holds.</param>
/// <param name="Total">How many cells there are.</param>
/// <param name="Mode">Which characters a cell takes.</param>
/// <param name="Private">Whether the cells mask what is typed.</param>
/// <param name="Disabled">Whether the cells take input at all.</param>
/// <param name="Invalid">Whether the cells draw the error state.</param>
/// <param name="Autocomplete">The first cell's <c>autocomplete</c>; the others are <c>off</c>.</param>
/// <param name="FirstId">The id the first cell carries, for the field's label.</param>
internal sealed record UiOtpScope(
    int Entered,
    int Total,
    Ui.OtpMode Mode,
    bool Private,
    bool Disabled,
    bool Invalid,
    string Autocomplete,
    string FirstId)
{
    /// <summary>The one cell Tab stops at: the first empty one, or the last of a full code.</summary>
    internal int Stop => Math.Min(Entered, Total - 1);
}
