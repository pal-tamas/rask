namespace Rask;

/// <summary>
///     Flux's <c>flux:navbar</c>: a row of <see cref="UiNavbarItem" />s — the links across a header.
/// </summary>
/// <remarks>A <c>&lt;nav&gt;</c> landmark, as Flux's is.</remarks>
public sealed partial class UiNavbar : Component
{
    /// <summary>Classes for the call site, added to the nav bar's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class("flex items-center gap-[2px] py-3", Class).Attributes(("data-ui-navbar", null))[Children ?? []];
}
