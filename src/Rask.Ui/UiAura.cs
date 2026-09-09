namespace Rask.Ui;

/// <summary>
/// A glow around something worth looking at.
/// </summary>
/// <remarks>
/// Decoration, and it says nothing — a reader who cannot see it loses nothing, so it carries no role and
/// no label. Use it on the one thing a surface is steering towards (the recommended plan, the primary
/// card) and not on several, since a page where everything glows has singled out nothing.
/// </remarks>
public sealed partial class UiAura : Component
{
    /// <summary>Which glow. Omitted, it is daisyUI's plain one.</summary>
    public UiAuraStyle? Style { get; set; }

    /// <summary>How far the glow reaches.</summary>
    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "aura",
            Style is { } style ? UiClassNames.AuraStyle(style) : "",
            Size is { } size ? UiClassNames.AuraSize(size) : "",
            Class))[
            Children ?? []
        ];
}
