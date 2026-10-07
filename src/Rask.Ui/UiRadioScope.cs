namespace Rask;

/// <summary>What a <see cref="UiRadioGroup{T}" /> tells the radios inside it.</summary>
/// <param name="GroupId">The group's id, which each radio's own id starts with.</param>
/// <param name="Name">The <c>name</c> the radios share: what makes them one choice to the browser.</param>
/// <param name="Variant">How the group draws its radios.</param>
/// <param name="Size">How tall a segment is.</param>
/// <param name="Indicator">Whether a card draws its dot.</param>
/// <param name="Invalid">Whether the group is drawn invalid.</param>
/// <param name="Current">The group's value.</param>
/// <param name="Choose">Makes a radio's value the group's.</param>
internal sealed record UiRadioScope(
    string GroupId,
    string Name,
    Ui.RadioGroupVariant Variant,
    Ui.RadioGroupSize Size,
    bool Indicator,
    bool Invalid,
    object? Current,
    Func<object?, Task> Choose);
