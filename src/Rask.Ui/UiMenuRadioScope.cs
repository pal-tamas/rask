namespace Rask;

/// <summary>What a <see cref="UiMenuRadioGroup{T}" /> tells the radios inside it: which is chosen, and how to choose.</summary>
/// <param name="IsChosen">Whether a radio's value is the group's.</param>
/// <param name="Choose">Makes a radio's value the group's.</param>
internal sealed record UiMenuRadioScope(Func<object?, bool> IsChosen, Func<object?, Task> Choose);
