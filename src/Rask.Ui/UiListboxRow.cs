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
        Div.Class(inList && option.Description is not null ? UiListboxLook.OptionBodyTall : UiListboxLook.OptionBody)[
            Div.Class(UiListboxLook.OptionLead)[
                Div.Class(UiListboxLook.Indicator)[Ui.Icon.Name(Ui.IconName.Check).Mini.Class(UiListboxLook.Check)],
                Lead(option)
            ],
            Div.Class(UiListboxLook.OptionText)[
                Div.Class(option.Avatar is null ? UiListboxLook.Words : UiListboxLook.WordsBesideAvatar)[Words(option, words)],
                option.Description is { } description ? P.Class(UiListboxLook.Description).Data("ui-text", "")[description] : null
            ]
        ];

    private static IEnumerable<Component?> Words(UiSelectOption option, string? words) =>
        words is null && option.HasCustomContent ? option.Children! : [words ?? option.Text];

    // The avatar wins over the icon, as Flux says.
    private static Component? Lead(UiSelectOption option)
    {
        if (option.Avatar is { } avatar)
        {
            return Avatar(avatar, option.Text);
        }

        return option.Icon is { } icon
            ? Ui.Icon.Name(icon).Variant(option.IconVariant ?? Ui.IconVariant.Mini).Class(UiClass.Compose(UiListboxLook.Icon, option.IconClass))
            : null;
    }

    // Flux's extra-small round avatar, as it measures inside an option. Ui.Avatar is not Flux's yet; when it
    // is, this is `Ui.Avatar.Src(…).Xs.Circle`.
    private static Component Avatar(string source, string name) =>
        Div.Class("relative flex-none isolate flex items-center justify-center size-6 me-2 rounded-full text-xs font-medium "
                  + "bg-zinc-200 dark:bg-zinc-600 text-zinc-800 dark:text-white "
                  + "after:absolute after:inset-0 after:rounded-full after:inset-ring-[1px] after:inset-ring-black/7 dark:after:inset-ring-white/10")
            .Data("ui-avatar", "")[
            Img.Src(source).Alt(name).Class("block size-full max-w-full rounded-full")
        ];
}
