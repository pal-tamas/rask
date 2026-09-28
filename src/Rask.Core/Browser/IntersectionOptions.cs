namespace Rask.Core.Browser;

/// <summary>Tuning for an observation (the <c>IntersectionObserver</c> options).</summary>
public sealed record IntersectionOptions
{
    /// <summary>
    ///     Visibility ratios at which the callback fires (e.g. <c>[0, 0.5, 1]</c>). Defaults to firing on
    ///     any enter/leave (<c>0</c>).
    /// </summary>
    public double[]? Thresholds { get; init; }

    /// <summary>Margin grown/shrunk around the root before testing intersection (CSS, e.g. <c>"200px"</c>).</summary>
    public string? RootMargin { get; init; }
}
