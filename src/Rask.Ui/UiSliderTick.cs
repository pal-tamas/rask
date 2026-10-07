namespace Rask;

/// <summary>
/// Flux UI's slider tick: a mark at one value of the <see cref="UiSlider{T}" /> it is inside.
/// </summary>
/// <remarks>
/// <c>Ui.SliderTick.Value(3)</c> draws a short line under the track — a dot with <c>.Dot</c> — and
/// <c>Ui.SliderTick.Value(3)["Mid"]</c> a label. Pressing a tick moves the thumb to its value (the nearer thumb
/// of a range). It carries <c>data-active</c> while the fill reaches it and <c>data-current</c> while a thumb is on it.
/// </remarks>
public sealed partial class UiSliderTick : Component
{
    /// <summary>The value the tick marks.</summary>
    public required double Value { get; set; }

    /// <summary>A line, or a dot. Drawn only when the tick has no label.</summary>
    public Ui.SliderTickVariant? Variant { get; set; }

    /// <summary>Classes for the tick.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiSliderScope>();
        var value = (decimal)Value;
        var tick = Div
            .Class(UiClass.Compose(UiSliderLook.Tick, scope?.Inside == true ? UiSliderLook.TickInside : UiSliderLook.TickBelow, Class))
            .Data(Marks(scope, value))
            .Style(scope is null ? null : "inset-inline-start:" + UiSliderLook.At(scope.Scale.Fraction(value)));

        if (scope?.Pick is { } pick)
        {
            tick = tick.OnClick(() => pick(value));
        }

        return tick[Children?.Any() == true ? Children : [Mark()]];
    }

    private HTMLSpanElement Mark() => Variant == Ui.SliderTickVariant.Dot
        ? Span.Class(UiSliderLook.TickDot).Data("ui-slider-tick-dot", "")
        : Span.Class(UiSliderLook.TickLine).Data("ui-slider-tick-line", "");

    private static Dictionary<string, string?> Marks(UiSliderScope? scope, decimal value)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-slider-tick"] = "",
            ["value"] = UiSliderScale.Text(value),
        };

        if (scope?.IsOn(value) == true)
        {
            marks["current"] = "";
        }

        if (scope?.Reaches(value) == true)
        {
            marks["active"] = "";
        }

        return marks;
    }
}
