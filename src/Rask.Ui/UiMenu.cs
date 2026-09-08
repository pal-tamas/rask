namespace Rask.Ui;

/// <summary>
/// A vertical list of links.
/// </summary>
/// <remarks>
/// A real <c>&lt;ul&gt;</c> of <c>&lt;li&gt;</c>: daisyUI's menu styles that shape, and it is also what
/// tells a screen reader how many items there are and which one it is on.
/// </remarks>
public sealed partial class UiMenu : Component
{
    public UiSize? Size { get; set; }

    /// <summary>Lays the items out in a row. daisyUI's <c>menu-horizontal</c>.</summary>
    public bool? Horizontal { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose(
            "menu",
            Horizontal == true ? "menu-horizontal" : "",
            Size is { } size ? UiClassNames.MenuSize(size) : "",
            Class))[Children ?? []];
}
