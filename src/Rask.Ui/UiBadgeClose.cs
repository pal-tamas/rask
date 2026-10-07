namespace Rask;

/// <summary>
///     Flux's <c>flux:badge.close</c>: the small button at the end of a <see cref="UiBadge" /> that removes it.
/// </summary>
/// <remarks>
///     A child of the badge, after its words: <c>Ui.Badge["Admin", Ui.BadgeClose.OnClick(Remove)]</c>. It IS
///     its <c>&lt;button&gt;</c>, so <c>OnClick</c> and the rest come from <see cref="Element" />. Half as
///     strong as the badge's text until the pointer is on it. Like Flux's it writes no accessible name: the
///     call site names it, <c>.AriaLabel("Remove Admin")</c>.
/// </remarks>
public sealed partial class UiBadgeClose : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-badge-close");

    /// <summary>The icon it shows. Unset, <see cref="Ui.IconName.XMark" />.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of the icon. Unset, <see cref="Ui.IconVariant.Micro" />.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <inheritdoc />
    protected override string TagName => "button";

    // The negative margins give its own padding back, so the badge is no taller or wider for holding it.
    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("p-1 -my-1 -me-1 opacity-50 hover:opacity-100", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override void WriteAttributes(System.Text.StringBuilder sb)
    {
        base.WriteAttributes(sb);
        AppendAttr(sb, "type", "button");
    }

    /// <inheritdoc />
    protected override IEnumerable<Component?> RenderChildren() =>
        [Ui.Icon.Name(Icon ?? Ui.IconName.XMark).Variant(IconVariant ?? Ui.IconVariant.Micro)];
}
