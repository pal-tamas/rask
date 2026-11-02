namespace Rask;

/// <summary>
///     What a select shows of the option it holds, apart from the option that said it: its words, its icon or
///     avatar, and what it posts.
/// </summary>
/// <remarks>
///     Flux stamps the picked option into its button and leaves it there while the value stands, whatever
///     options a later render brings — a search the page answers with other rows. The option itself cannot be
///     kept for that: the next render hands the same instance to another row. So its face is. Content that is
///     more than words (a swatch beside a name) is kept as its words.
/// </remarks>
internal sealed record UiOptionFace(
    string FormValue,
    string Text,
    string SelectedText,
    Component? Avatar,
    Ui.IconName? Icon,
    Ui.IconVariant? IconVariant,
    string? IconClass,
    string? Description)
{
    /// <summary>The face of <paramref name="option" /> as it is drawn now.</summary>
    /// <param name="option">The option.</param>
    internal static UiOptionFace Of(UiSelectOption option) =>
        new(option.FormValue, option.Text, option.SelectedText, option.Avatar, option.Icon, option.IconVariant, option.IconClass, option.Description);

    /// <summary>Whether <paramref name="option" /> still shows this face: nothing to keep anew.</summary>
    /// <param name="option">The option.</param>
    internal bool Is(UiSelectOption option) =>
        string.Equals(FormValue, option.FormValue, StringComparison.Ordinal)
        && string.Equals(Text, option.Text, StringComparison.Ordinal)
        && string.Equals(SelectedText, option.SelectedText, StringComparison.Ordinal)
        && SameAvatar(Avatar, option.Avatar)
        && Icon == option.Icon
        && IconVariant == option.IconVariant
        && string.Equals(IconClass, option.IconClass, StringComparison.Ordinal)
        && string.Equals(Description, option.Description, StringComparison.Ordinal);

    // An image's address is the same avatar when it spells the same; an avatar of the page's own, when it is the same one.
    private static bool SameAvatar(Component? kept, Component? now) => (kept, now) switch
    {
        (null, null) => true,
        (Rask.Core.Components.Text a, Rask.Core.Components.Text b) => string.Equals(a.Value, b.Value, StringComparison.Ordinal),
        _ => ReferenceEquals(kept, now),
    };
}
