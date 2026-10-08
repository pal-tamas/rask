namespace Rask;

/// <summary>Flux's <c>flux:sidebar.profile</c>: who is signed in, at the foot of a <see cref="UiSidebar" />.</summary>
/// <remarks>
/// A button: the trigger of the account menu, which is the call site's to put around it. Narrowed to the
/// rail it is the avatar alone.
/// </remarks>
public sealed partial class UiSidebarProfile : Component, IUiTrigger
{
    /// <summary>The picture's URL. Without one the name's initials stand in.</summary>
    public string? Avatar { get; set; }

    /// <summary>The person's name.</summary>
    public string? Name { get; set; }

    /// <summary>Classes for the profile.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    private UiInvoked? _invoked;

    /// <inheritdoc />
    Component IUiTrigger.Invoking(string panelId, bool open)
    {
        _invoked = new UiInvoked(panelId, open);
        // What a generated step does when it writes a new value: the row is drawn again with it.
        BuilderRuntime.MarkChanged(this);

        return this;
    }

    protected override Component? Render()
    {
        var button = Button.Type(ButtonType.Button)
            .Class(UiClass.Compose(
                "group flex w-full cursor-default items-center rounded-lg p-1 hover:bg-zinc-800/5 dark:hover:bg-white/10",
                Class))
            .Attributes(("data-ui-sidebar-profile", ""));

        return (_invoked is { } invoked ? invoked.On(button) : button)[
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

    // Flux's avatar, at its `sm` size: the picture, or the initials of the name.
    private UiAvatar Face() => Ui.Avatar.Sm.Name(Name).Src(Avatar);
}
