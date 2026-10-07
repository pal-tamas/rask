namespace Rask;

/// <summary>What a <see cref="UiChart" /> hands the parts inside it: its rows, its orientation and its tooltip.</summary>
internal sealed record UiChartScope(UiChartData? Data, bool Horizontal, UiChartTooltip? Tooltip);
