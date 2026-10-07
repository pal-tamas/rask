namespace Rask;

/// <summary>What the labels along a chart's index axis did when they would not fit side by side.</summary>
internal enum UiChartCrowding
{
    /// <summary>They fit.</summary>
    None = 0,

    /// <summary>Dates: every second one is hidden.</summary>
    EveryOther,

    /// <summary>Dates, still crowded: only the first and the last, each aligned to its own end.</summary>
    Ends,

    /// <summary>Names: all of them, turned 45°.</summary>
    Turned,
}
