namespace Rask.Ui;

/// <summary>
/// A list of places to go — a sidebar's navigation, a settings page's sections.
/// </summary>
/// <remarks>
/// Flux UI's navlist: a <c>&lt;nav&gt;</c> landmark holding daisyUI's <c>menu</c>, with <see cref="UiNavItem" />s and
/// <see cref="UiNavGroup" />s inside. A page with more than one navigation landmark needs each named, which is what
/// <see cref="AccessibleLabel" /> is for — "Main", "Settings".
/// </remarks>
public sealed partial class UiNavList : Component
{
    /// <summary>The landmark's name, for a page with more than one navigation.</summary>
    public string? AccessibleLabel { get; set; }

    /// <summary>Row density, as on a menu.</summary>
    public UiSize? Size { get; set; }

    /// <summary>
    ///     Draws the current row as an outlined pill rather than a filled one — Flux UI's <c>outline</c> navlist.
    /// </summary>
    /// <remarks>
    ///     For a navigation that sits on a coloured ground, where a filled current row disappears into it. The only
    ///     variant daisyUI's menu has a rule for; the other <see cref="UiVariant" /> members would name classes
    ///     that do not exist, so this is a flag rather than the shared axis.
    /// </remarks>
    public bool? Outline { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var nav = Nav.Class(Class);
        if (AccessibleLabel is { } label)
        {
            nav = nav.Aria("label", label);
        }

        return nav[
            Ul.Class(UiClass.Compose(
                "menu w-full p-0",
                Size is { } size ? UiClassNames.MenuSize(size) : "",
                Outline == true ? "ui-navlist-outline" : ""))[
                Children ?? []
            ]
        ];
    }
}
