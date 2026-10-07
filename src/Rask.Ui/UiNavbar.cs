namespace Rask;

/// <summary>
///     Flux's <c>flux:navbar</c>: a row of <see cref="UiNavbarItem" />s — the links across a header.
/// </summary>
/// <remarks>
///     A <c>&lt;nav&gt;</c> landmark. A page with more than one needs each named, which is what
///     <see cref="AccessibleLabel" /> is for — "Main", "Account".
/// </remarks>
public sealed partial class UiNavbar : Component
{
    /// <summary>The landmark's name, for a page with more than one navigation.</summary>
    public string? AccessibleLabel { get; set; }

    /// <summary>Classes for the call site, added to the navbar's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var nav = Nav.Class("flex items-center gap-[2px] py-3", Class);
        if (AccessibleLabel is { } label)
        {
            nav = nav.Aria("label", label);
        }

        return nav.Attributes(("data-ui-navbar", null))[Children ?? []];
    }
}
