using System.Runtime.InteropServices;

namespace Rask;

/// <summary>How far a chart's labels and points would reach past each edge of its box, were the plot to fill it.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct UiChartReach
{
    internal double Top;
    internal double Right;
    internal double Bottom;
    internal double Left;
}
