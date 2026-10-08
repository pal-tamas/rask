namespace Rask;

/// <summary>
///     Flux's <c>flux:profile</c>: the signed-in person as a button — their avatar, optionally their name, and
///     a chevron that says it opens something.
/// </summary>
/// <remarks>
///     <para>
///     A <c>&lt;button&gt;</c> and nothing more: it is the TRIGGER of an account menu or a profile switcher,
///     and the menu is the dropdown's: <c>Ui.Dropdown[Ui.Profile.Name("Ada"), Ui.Menu[…]]</c>.
///     </para>
///     <para>
///     <see cref="Avatar" /> is an image's address, or an avatar of your own; without one the initials are
///     worked out from <see cref="Name" />, or from <see cref="AvatarName" /> for a profile that shows no name.
///     </para>
/// </remarks>
public sealed partial class UiProfile : Component, IUiTrigger
{
    private const string Base =
        "group flex items-center p-1 hover:bg-zinc-800/5 dark:hover:bg-white/15";

    private const string Ink =
        "text-zinc-400 group-hover:text-zinc-800 dark:text-white/80 dark:group-hover:text-white";

    /// <summary>The person's name, beside the avatar.</summary>
    public string? Name { get; set; }

    /// <summary>The avatar: a string is an image's address, anything else is drawn as it is.</summary>
    public Component? Avatar { get; set; }

    /// <summary>The name the initials are worked out from, for a profile that shows none.</summary>
    public string? AvatarName { get; set; }

    /// <summary>The fill behind the initials.</summary>
    public Ui.Color? AvatarColor { get; set; }

    /// <summary>A circular avatar, and a button rounded to match.</summary>
    public bool? Circle { get; set; }

    /// <summary>The initials to show, rather than those worked out from the name.</summary>
    public string? Initials { get; set; }

    /// <summary>The chevron after the name. Shown unless this is <see langword="false" />.</summary>
    public bool? Chevron { get; set; }

    /// <summary>An icon in place of the chevron.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>Which drawing of the trailing icon. Micro when unset.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>Classes for the call site, added to the profile's own.</summary>
    public string? Class { get; set; }

    private UiInvoked? _invoked;

    /// <inheritdoc />
    Component IUiTrigger.Invoking(string panelId, bool open)
    {
        _invoked = new UiInvoked(panelId, open);
        // What a generated step does when it writes a new value: the component is drawn again with it.
        BuilderRuntime.MarkChanged(this);

        return this;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // The marker first: `Attributes` replaces the bag, and what makes the row a dropdown's trigger is in it.
        var button = Button.Type(ButtonType.Button)
            .Class(Base, Circle == true ? "rounded-full" : "rounded-lg", Class)
            .Attributes(("data-ui-profile", null));

        return (_invoked is { } invoked ? invoked.On(button) : button)[
            Div.Class("shrink-0")[Face()],
            Name is { } name
                ? Span.Class("ui-rail-hide mx-2 truncate text-sm font-medium text-zinc-500 group-hover:text-zinc-800 dark:text-white/80 dark:group-hover:text-white")[name]
                : null,
            Chevron == false && IconTrailing is null
                ? null
                : Div.Class("ui-rail-hide ms-auto flex size-8 shrink-0 items-center justify-center")[
                    Ui.Icon.Name(IconTrailing ?? Ui.IconName.ChevronDown).Variant(IconVariant ?? Ui.IconVariant.Micro).Class(Ink)
                ]
        ];
    }

    private Component Face()
    {
        if (Avatar is { } own and not global::Rask.Core.Components.Text)
        {
            return own;
        }

        return Ui.Avatar.Sm
            .Name(AvatarName ?? Name)
            .Initials(Initials)
            .Circle(Circle)
            .Color(AvatarColor)
            .Src((Avatar as global::Rask.Core.Components.Text)?.Value);
    }
}
