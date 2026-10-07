using System.Runtime.InteropServices;

namespace Rask;

/// <summary>One tick label: where along its axis (0 to 1), what it says, and how wide that is in Inter.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct UiChartLabel(double Unit, string Text, double Width);
