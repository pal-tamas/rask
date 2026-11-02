using Rask.Core.Components;

namespace Rask;

/// <summary>
///     The avatar at the start of a select's option: Flux's <c>avatar</c> and the <c>avatar:*</c> props it forwards
///     to <c>flux:avatar</c>.
/// </summary>
/// <remarks>
///     A string is the image's address, drawn as Flux draws it there: extra small, round, named by the option. An
///     avatar of the page's own (<c>Ui.Avatar.ColorAuto()</c>) is Flux's <c>avatar:*</c>: what it states wins, and
///     what it leaves unset is the option's. It is drawn as a copy, never itself: an option shows it twice, in its
///     row and in the select's button, and keeps it after the option is gone.
/// </remarks>
internal static class UiOptionAvatar
{
    // The room an option leaves after its avatar.
    private const string Room = "me-2 isolate";

    /// <summary>The avatar <paramref name="avatar" /> stands for, in the option named <paramref name="name" />.</summary>
    /// <param name="avatar">The option's <c>Avatar</c>.</param>
    /// <param name="name">The option's words.</param>
    internal static Component Draw(Component avatar, string name) => avatar switch
    {
        Text address => Ui.Avatar.Xs.Circle().Src(address.Value).Name(name).Class(Room),
        UiAvatar own => Forwarded(own, name),
        _ => avatar,
    };

    // Every prop of the page's avatar, onto a new one; the option fills the ones it leaves unset.
    private static Component Forwarded(UiAvatar own, string name)
    {
        var copy = Ui.Avatar
            .Name(own.Name ?? name)
            .Src(own.Src)
            .Initials(own.Initials)
            .InitialsSingle(own.InitialsSingle)
            .Alt(own.Alt)
            .Size(own.Size ?? Ui.AvatarSize.Xs)
            .Color(own.Color)
            .ColorAuto(own.ColorAuto)
            .ColorSeed(own.ColorSeed)
            .Circle(own.Circle ?? true)
            .Icon(own.Icon)
            .IconVariant(own.IconVariant)
            .Tooltip(own.Tooltip)
            .TooltipPosition(own.TooltipPosition)
            .Badge(own.Badge)
            .BadgeColor(own.BadgeColor)
            .BadgeCircle(own.BadgeCircle)
            .BadgePosition(own.BadgePosition)
            .BadgeVariant(own.BadgeVariant)
            .As(own.As)
            .Href(own.Href)
            .Class(UiClass.Compose(Room, own.Class));

        return own.Children is { } children ? copy[children] : copy;
    }
}
