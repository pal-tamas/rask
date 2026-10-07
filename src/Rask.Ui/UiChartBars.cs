namespace Rask;

/// <summary>The bars declared inside a <see cref="UiChartGroup" /> or a <see cref="UiChartStack" />.</summary>
internal static class UiChartBars
{
    internal static List<UiChartBar> Bars(this Component container)
    {
        var bars = new List<UiChartBar>();
        foreach (var child in container.Children ?? [])
        {
            if (child is UiChartBar bar)
            {
                bars.Add(bar);
            }
        }

        return bars;
    }
}
