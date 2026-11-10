namespace Rask;

/// <summary>
/// What a <see cref="UiChart" /> hands the parts inside it: its rows, its orientation, its pie if it draws one, and the
/// parts beside the drawing that name its rows and series — a tooltip's heading and values, a legend's entries — which
/// the drawing's description for a screen reader reads its names from (<see cref="UiChartDescription" />).
/// </summary>
internal sealed record UiChartScope(UiChartData? Data, bool Horizontal, UiChartPie? Pie, IReadOnlyList<Component> Named);
