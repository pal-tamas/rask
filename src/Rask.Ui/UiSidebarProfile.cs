namespace Rask;

/// <summary>Flux's <c>flux:sidebar.profile</c>: who is signed in, at the foot of a <see cref="UiSidebar" />.</summary>
/// <remarks>
/// A button: the trigger of the account menu, which is the call site's to put around it. Narrowed to the
/// rail it is the avatar alone.
/// </remarks>
public sealed partial class UiSidebarProfile : Component
{
    /// <summary>The picture's URL. Without one the name's initials stand in.</summary>
    public string? Avatar { get; set; }

    /// <summary>The person's name.</summary>
    public string? Name { get; set; }

    /// <summary>Runs when the profile is pressed.</summary>
    public Callback OnClick { get; set; }

    /// <summary>Classes for the profile.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var button = Button.Type(ButtonType.Button)
            .Class(UiClass.Compose(
                "group flex w-full cursor-default items-center rounded-lg p-1 hover:bg-zinc-800/5 dark:hover:bg-white/10",
                Class))
            .Attributes(UiMarks.Present(("data-ui-sidebar-profile", ""), ("title", Name)));

        return (OnClick.HasValue ? button.OnClick(OnClick) : button)[
            Div.Class("shrink-0")[Face()],
            Name is { Length: > 0 } name
                ? Span.Class(
                    "mx-2 block truncate text-sm font-medium text-zinc-500 group-hover:text-zinc-800 sidebar-rail:sr-only "
                    + "dark:text-white/80 dark:group-hover:text-white")[name]
                : null,
            Div.Class("ms-auto flex size-8 shrink-0 items-center justify-center sidebar-rail:hidden")[
                Ui.Icon.Name(Ui.IconName.ChevronDown).Micro.Class("text-zinc-400 dark:text-white/80 group-hover:text-zinc-800 dark:group-hover:text-white")
            ]
        ];
    }

    // Seam: Flux's avatar, at its `sm` size. Ui.Avatar goes here when it lands.
    private Component Face() =>
        Div.Class(
            "relative flex size-8 shrink-0 items-center justify-center rounded-md bg-zinc-200 text-sm font-medium "
            + "text-zinc-800 after:absolute after:inset-0 after:rounded-md after:inset-ring-[1px] "
            + "after:inset-ring-black/7 dark:bg-zinc-600 dark:text-white dark:after:inset-ring-white/10")
            .Attributes(("data-ui-seam", "avatar"))[
            Avatar is { Length: > 0 } src
                ? Img.Src(src).Alt(Name ?? "").Class("rounded-md")
                : Span.Aria("hidden", "true")[global::Rask.UiAvatar.Initials(Name ?? "")]
        ];
}
