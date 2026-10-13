using static Rask.Markup;

namespace Rask;

/// <summary>
///     What one option holds, as Flux lays it out: the tick's place, an icon or an avatar, the words and the
///     line under them.
/// </summary>
/// <remarks>
///     Drawn twice for the picked option — in its row and in the select's button — and the same both times:
///     the button hides the tick and the description by CSS, as Flux's does with the copy its script makes.
/// </remarks>
internal static class UiListboxRow
{
    /// <summary>The content of <paramref name="option" />'s row.</summary>
    /// <param name="option">The option.</param>
    /// <param name="words">Words in place of the option's own: its <c>SelectedLabel</c>, in the button.</param>
    /// <param name="inList">False for the copy in the select's button.</param>
    internal static Component Content(UiSelectOption option, string? words = null, bool inList = true) =>
        Content(
            option.Avatar,
            option.Icon,
            option.IconVariant,
            option.IconClass,
            option.Description,
            option.Text,
            words is null && option.HasCustomContent ? option.Children! : [words ?? option.Text],
            inList);

    /// <summary>The button's copy of an option that is no longer among the rows, from the face the select kept.</summary>
    /// <param name="face">What the option showed when it was last drawn.</param>
    internal static Component Content(UiOptionFace face) =>
        Content(face.Avatar, face.Icon, face.IconVariant, face.IconClass, face.Description, face.Text, [face.SelectedText], inList: false);

    private static Component Content(
        string? avatar, Ui.IconName? icon, Ui.IconVariant? iconVariant, string? iconClass, string? description, string name,
        IEnumerable<Component?> words, bool inList) =>
        Div.Class(inList && description is not null ? UiListboxLook.OptionBodyTall : UiListboxLook.OptionBody)[
            Div.Class(UiListboxLook.OptionLead)[
                Div.Class(UiListboxLook.Indicator)[Ui.Icon.Name(Ui.IconName.Check).Mini.Class(UiListboxLook.Check)],
                Lead(avatar, icon, iconVariant, iconClass, name)
            ],
            Div.Class(UiListboxLook.OptionText)[
                Div.Class(avatar is null ? UiListboxLook.Words : UiListboxLook.WordsBesideAvatar)[words],
                description is null ? null : P.Class(UiListboxLook.Description).Data("ui-text", "")[description]
            ]
        ];

    // The avatar wins over the icon, as Flux says.
    private static Component? Lead(string? avatar, Ui.IconName? icon, Ui.IconVariant? iconVariant, string? iconClass, string name)
    {
        if (avatar is not null)
        {
            return Avatar(avatar, name);
        }

        return icon is { } drawn
            ? Ui.Icon.Name(drawn).Variant(iconVariant ?? Ui.IconVariant.Mini).Class(UiClass.Compose(UiListboxLook.Icon, iconClass))
            : null;
    }

    // Flux's extra-small round avatar, with the room an option leaves after it.
    private static UiAvatar Avatar(string source, string name) =>
        Ui.Avatar.Xs.Circle().Src(source).Name(name).Class("me-2 isolate");
}
