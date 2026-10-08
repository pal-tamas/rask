namespace Rask;

/// <summary>
///     Flux's <c>flux:avatar.group</c>: <see cref="UiAvatar" />s stacked, each overlapping the one before and
///     ringed in the colour of the page so the stack reads as separate faces.
/// </summary>
/// <remarks>
///     The ring is white, and zinc-900 on dark — the grounds Flux's pages sit on. On any other ground, name
///     its colour in <see cref="Class" /> (<c>*:ring-zinc-100 dark:*:ring-zinc-800</c>), as Flux does.
/// </remarks>
public sealed partial class UiAvatarGroup : Component
{
    /// <summary>Classes for the call site, added to the group's own — a ring colour for another ground.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("isolate flex *:ring-4 *:ring-white *:not-first:-ms-2 dark:*:ring-zinc-900", Class)[Children ?? []];
}
