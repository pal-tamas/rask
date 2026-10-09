namespace Rask;

/// <summary>
/// One figure of a <see cref="UiChartSummary" />: the latest row's value of a field. Flux UI's
/// <c>chart.summary.value</c>.
/// </summary>
public sealed partial class UiChartSummaryValue : Component, IUiChartField
{
    /// <summary>What is shown: <c>.Field((Sale s) =&gt; s.Amount)</c>.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>What is shown when there is no row to read.</summary>
    public string? Fallback { get; set; }

    /// <summary>How the value is written.</summary>
    public UiChartFormat? Format { get; set; }

    private readonly UiChartLines _lines = new();

    // What it shows is one of the chart's rows, which arrive through the context rather than as a prop of its own.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var data = Context.Get<UiChartScope>()?.Data;
        // Flux's markup: a <span> holding a <slot>, which outside a shadow tree simply shows what is in it.
        // At rest it reads the latest row; under the pointer the plot hook shows the hovered row's line instead.
        return Span[
            Slot.Data(_lines.Of(data, Field, Format))[
                data is null || data.Count == 0 ? Fallback : UiChartWriting.Value(Field, Format, data, data.Count - 1)]];
    }
}
