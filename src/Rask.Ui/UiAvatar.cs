using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:avatar</c>: a person as a picture, or as their initials or an icon when there is none.
/// </summary>
/// <remarks>
///     <para>
///     <see cref="Src" /> draws the image. Without one, <see cref="Icon" /> draws an icon, and failing that the
///     initials — <see cref="Initials" /> as given, or worked out from <see cref="Name" />: the first letter of
///     the first and last words ("Caleb Porzio" is CP), the first two letters of a single word ("calebporzio" is
///     Ca), or one letter with <see cref="InitialsSingle" />. Children replace all of that ("3+").
///     </para>
///     <para>
///     <see cref="Color" /> fills it with a hue; <see cref="ColorAuto" /> picks one of the seventeen from the
///     initials (or from <see cref="ColorSeed" />, for something that never changes, like an id), so the same
///     person is the same colour everywhere.
///     </para>
///     <para>
///     A <c>&lt;div&gt;</c> unless it does something: <see cref="Href" /> makes it a link and
///     <see cref="Ui.AvatarAs.Button" /> a button. <see cref="Badge" /> puts a mark on a corner — an empty one
///     is the plain dot — and <see cref="Tooltip" /> names it on hover.
///     </para>
/// </remarks>
public sealed partial class UiAvatar : Component, IUiTrigger
{
    private const string Frame =
        "relative flex shrink-0 items-center justify-center font-medium after:absolute after:inset-0 "
        + "after:rounded-[inherit] after:inset-ring-1 after:inset-ring-black/7 dark:after:inset-ring-white/10";

    private const string Plain = "bg-zinc-200 text-zinc-800 dark:bg-zinc-600 dark:text-white";

    private const string BadgeBase =
        "absolute z-10 flex h-3 min-w-3 items-center justify-center overflow-hidden text-[.625rem] tabular-nums ring-2 "
        + "ring-white dark:ring-zinc-900";

    /// <summary>Whose avatar it is: the source of the initials, the image's alternative text, and the tooltip's words.</summary>
    public string? Name { get; set; }

    /// <summary>The image's address.</summary>
    public string? Src { get; set; }

    /// <summary>The initials to show, rather than those worked out from <see cref="Name" />.</summary>
    public string? Initials { get; set; }

    /// <summary>One letter rather than two.</summary>
    public bool? InitialsSingle { get; set; }

    /// <summary>The image's alternative text. <see cref="Name" /> when unset.</summary>
    public string? Alt { get; set; }

    /// <summary>How large it is. 40px when unset.</summary>
    public Ui.AvatarSize? Size { get; set; }

    /// <summary>The fill behind initials or an icon. Zinc when unset.</summary>
    public Ui.Color? Color { get; set; }

    /// <summary>Picks the fill from the initials, so one person is always one colour. Flux's <c>color="auto"</c>.</summary>
    public bool? ColorAuto { get; set; }

    /// <summary>What <see cref="ColorAuto" /> picks from instead of the initials — an id that never changes.</summary>
    public string? ColorSeed { get; set; }

    /// <summary>A circle rather than a rounded square.</summary>
    public bool? Circle { get; set; }

    /// <summary>An icon, in place of initials.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of the icon. Solid when unset.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>Words shown on hover and keyboard focus.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Which side the tooltip opens on. Above when unset.</summary>
    public Ui.TooltipPosition? TooltipPosition { get; set; }

    /// <summary>A mark on one corner: a count, an emoji, any content. An empty string is the plain dot.</summary>
    public Component? Badge { get; set; }

    /// <summary>The badge's colour. The page's own ground when unset.</summary>
    public Ui.Color? BadgeColor { get; set; }

    /// <summary>A round badge rather than a slightly rounded one.</summary>
    public bool? BadgeCircle { get; set; }

    /// <summary>Which corner the badge sits on. Bottom right when unset.</summary>
    public Ui.AvatarBadgePosition? BadgePosition { get; set; }

    /// <summary>How the badge is drawn.</summary>
    public Ui.AvatarBadgeVariant? BadgeVariant { get; set; }

    /// <summary>The element it is written as, when it is not a link.</summary>
    public Ui.AvatarAs? As { get; set; }

    /// <summary>Where it goes, which makes it a link.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>Classes for the call site, added to the avatar's own.</summary>
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
        var size = Size ?? Ui.AvatarSize.Md;
        var initials = Initials ?? UiAvatarInitials.From(Name, InitialsSingle == true);
        var classes = UiClass.Compose(Frame, SizeClass(size, Circle == true), Fill(initials), Class);
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-ui-avatar"] = null,
            ["data-slot"] = "avatar",
            ["data-size"] = SizeName(size),
        };
        if (Circle == true)
        {
            marks["data-circle"] = "true";
        }

        Component?[] content = [Face(size, initials), Mark()];
        var avatar = Element(classes, marks, content);

        // The seam with the tooltip component: the avatar hands it the words and the side, nothing else.
        return Tooltip is { Length: > 0 } tip
            ? Ui.Tooltip.Content(tip).Position(TooltipPosition)[avatar]
            : avatar;
    }

    private Component Element(string classes, Dictionary<string, string?> marks, Component?[] content)
    {
        if (Href is not { } href)
        {
            if (As != Ui.AvatarAs.Button)
            {
                return Div.Class(classes).Attributes(marks)[content];
            }

            var button = Button.Type(ButtonType.Button).Class(classes).Attributes(marks);
            return (_invoked is { } invoked ? invoked.On(button) : button)[content];
        }

        return href.PageType is null
            ? A.Href(href.ToString()).Class(classes).Attributes(marks)[content]
            : NavLink.Href(href).ActiveClass("").Class(classes).Attributes(marks)[content];
    }

    private Component? Face(Ui.AvatarSize size, string initials)
    {
        if (Src is { Length: > 0 } src)
        {
            return Img.Src(src).Alt(Alt ?? Name ?? "").Class("size-full max-w-full rounded-[inherit] object-cover");
        }

        if (Children is { } children)
        {
            return Span.Class("select-none")[children];
        }

        if (Icon is { } icon)
        {
            return Ui.Icon.Name(icon).Variant(IconVariant ?? Ui.IconVariant.Solid).Class(IconClass(size));
        }

        return initials.Length == 0 ? null : Span.Class("select-none")[initials];
    }

    private Component? Mark()
    {
        if (Badge is not { } badge)
        {
            return null;
        }

        var outline = BadgeVariant == Ui.AvatarBadgeVariant.Outline;
        return Div.Class(
                BadgeBase,
                BadgeCircle == true ? "rounded-full" : "rounded-[3px]",
                Corner(BadgePosition ?? Ui.AvatarBadgePosition.BottomRight),
                BadgeColor is { } color ? UiNavColors.AvatarBadge(color) : "bg-white dark:bg-zinc-900",
                outline ? "after:absolute after:inset-[3px] after:rounded-full after:bg-white dark:after:bg-zinc-900" : "")
            .Aria("hidden", "true")[badge];
    }

    // No hue named: the plain zinc tile. `ColorAuto` hashes what the reader sees, so one person is one colour.
    private string Fill(string initials)
    {
        if (Color is { } color)
        {
            return UiNavColors.Avatar(color);
        }

        return ColorAuto == true ? UiNavColors.Avatar(UiAvatarInitials.Hue(ColorSeed ?? initials)) : Plain;
    }

    private static string SizeName(Ui.AvatarSize size) => size switch
    {
        Ui.AvatarSize.Xs => "xs",
        Ui.AvatarSize.Sm => "sm",
        Ui.AvatarSize.Lg => "lg",
        Ui.AvatarSize.Xl => "xl",
        _ => "md",
    };

    private static string SizeClass(Ui.AvatarSize size, bool circle) => (size, circle) switch
    {
        (Ui.AvatarSize.Xs, false) => "size-6 rounded-sm text-xs",
        (Ui.AvatarSize.Sm, false) => "size-8 rounded-md text-sm",
        (Ui.AvatarSize.Lg, false) => "size-12 rounded-lg text-base",
        (Ui.AvatarSize.Xl, false) => "size-16 rounded-xl text-base",
        (_, false) => "size-10 rounded-lg text-sm",
        (Ui.AvatarSize.Xs, true) => "size-6 rounded-full text-xs",
        (Ui.AvatarSize.Sm, true) => "size-8 rounded-full text-sm",
        (Ui.AvatarSize.Lg, true) => "size-12 rounded-full text-base",
        (Ui.AvatarSize.Xl, true) => "size-16 rounded-full text-base",
        (_, true) => "size-10 rounded-full text-sm",
    };

    private static string IconClass(Ui.AvatarSize size) => size switch
    {
        Ui.AvatarSize.Xs => "size-4 opacity-75",
        Ui.AvatarSize.Sm => "size-5 opacity-75",
        Ui.AvatarSize.Lg => "size-8 opacity-75",
        Ui.AvatarSize.Xl => "size-10 opacity-75",
        _ => "size-6 opacity-75",
    };

    private static string Corner(Ui.AvatarBadgePosition position) => position switch
    {
        Ui.AvatarBadgePosition.TopLeft => "start-0 top-0",
        Ui.AvatarBadgePosition.TopRight => "end-0 top-0",
        Ui.AvatarBadgePosition.BottomLeft => "start-0 bottom-0",
        _ => "end-0 bottom-0",
    };
}
