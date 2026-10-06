namespace Rask;

/// <summary>
///     Flux's <c>flux:badge.close</c>: the small button at the end of a <see cref="UiBadge" /> that removes it.
/// </summary>
/// <remarks>
///     A child of the badge, after its words: <c>Ui.Badge["Admin", Ui.BadgeClose.OnClick(Remove)]</c>. It IS
///     its <c>&lt;button&gt;</c>, so <c>OnClick</c> and the rest come from <see cref="Element" />. Half as
///     strong as the badge's text until the pointer is on it, and named "Remove" for a screen reader unless
///     the call site names it: <c>.Aria("label", "Remove Admin")</c>.
/// </remarks>
public sealed partial class UiBadgeClose : UiElement
{
    private static readonly Dictionary<string, string?> Marker = new(StringComparer.Ordinal) { ["ui-badge-close"] = null };

    private static readonly Dictionary<string, string?> Named = new(StringComparer.Ordinal) { ["label"] = "Remove" };

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
    private protected override IReadOnlyDictionary<string, string?> ResolveData()
    {
        if (Data is not { Count: > 0 } own)
        {
            return Marker;
        }

        var data = new Dictionary<string, string?>(Marker, StringComparer.Ordinal);
        foreach (var (name, value) in own)
        {
            data[name] = value;
        }

        return data;
    }

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?>? ResolveAria()
    {
        if (Aria is not { Count: > 0 } own)
        {
            return Named;
        }

        if (own.ContainsKey("label") || own.ContainsKey("labelledby"))
        {
            return own;
        }

        var aria = new Dictionary<string, string?>(Named, StringComparer.Ordinal);
        foreach (var (name, value) in own)
        {
            aria[name] = value;
        }

        return aria;
    }

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
