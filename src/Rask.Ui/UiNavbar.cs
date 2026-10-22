namespace Rask;

/// <summary>
///     Flux's <c>flux:navbar</c>: a row of <see cref="UiNavbarItem" />s — the links across a header.
/// </summary>
/// <remarks>A <c>&lt;nav&gt;</c> landmark, as Flux's is.</remarks>
public sealed partial class UiNavbar : Component
{
    /// <summary>
    ///     Scrolls sideways when its items are wider than it, rather than pushing the page wider. Flux's
    ///     <c>scrollable</c>, which its layout pages write on a header's row of tabs.
    /// </summary>
    public bool? Scrollable { get; set; }

    /// <summary>Classes for the call site, added to the nav bar's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class("flex items-center gap-[2px] py-3", Scrollable == true ? "overflow-x-auto overflow-y-hidden" : null, Class)
            .Attributes(("data-ui-navbar", null))[Children ?? []];
}
