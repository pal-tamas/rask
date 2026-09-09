namespace Rask.Ui;

/// <summary>
/// A hint shown on hover or focus.
/// </summary>
/// <remarks>
/// CSS-only, from daisyUI's <c>data-tip</c>. It is a hint and nothing more: a tooltip is not reachable by
/// touch and is easy to miss, so nothing that matters should live only here.
/// </remarks>
public sealed partial class UiTooltip : Div
{
    public required string Tip { get; set; }

    /// <summary>Which side of the thing it points at. All seven are defined for a tooltip.</summary>
    public UiPlacement? Placement { get; set; }

    /// <summary>Anything but <see cref="UiTone.Neutral" />, which daisyUI does not define for a tooltip.</summary>
    public UiTone? Tone { get; set; }

    /// <summary>
    ///     Shows it without waiting for a hover. For walking someone through a screen — and the only way
    ///     a touch user ever sees one, since there is no hover on a touch screen.
    /// </summary>
    public bool? Open { get; set; }

    /// <inheritdoc />
    protected override void WriteAttributes(System.Text.StringBuilder sb)
    {
        base.WriteAttributes(sb);
        AppendAttr(sb, "data-tip", Tip);
    }

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "tooltip",
            Placement is { } placement ? UiClassNames.TooltipPlacement(placement) : "",
            Tone is { } tone ? UiClassNames.TooltipTone(tone) : "",
            Open == true ? "tooltip-open" : "",
            Class);
}
