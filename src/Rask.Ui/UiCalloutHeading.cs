namespace Rask;

/// <summary>
///     Flux's <c>flux:callout.heading</c>: the line a <see cref="UiCallout" /> leads with.
/// </summary>
/// <remarks>
///     Its colour is its callout's. An <see cref="Icon" /> here instead of on the callout puts the icon in
///     the heading's own line, which is the compact layout: the text under it starts at the callout's edge.
/// </remarks>
public sealed partial class UiCalloutHeading : Component
{
    private static readonly Dictionary<string, string?> Slot = new(StringComparer.Ordinal) { ["slot"] = "heading" };

    /// <summary>An icon in the heading's line, instead of beside the whole callout.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of <see cref="Icon" />. <see cref="Ui.IconVariant.Mini" /> when unset.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>An icon of your own in place of <see cref="Icon" />: Flux's <c>icon</c> slot.</summary>
    public Component? CustomIcon { get; set; }

    /// <summary>Classes for the call site, added to the heading's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("flex items-center gap-2 text-sm font-medium", Class)).Data(Slot)[
            CustomIcon ?? (Icon is { } name ? Ui.Icon.Name(name).Variant(IconVariant ?? Ui.IconVariant.Mini) : null),
            Children ?? []
        ];
}
