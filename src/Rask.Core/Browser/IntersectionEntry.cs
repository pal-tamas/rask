namespace Rask.Core.Browser;

/// <summary>One intersection notification for an observed element.</summary>
/// <param name="IsIntersecting">Whether the element currently intersects the viewport/root.</param>
/// <param name="Ratio">How much of the element is visible, <c>0</c>–<c>1</c> (<c>intersectionRatio</c>).</param>
public sealed record IntersectionEntry(bool IsIntersecting, double Ratio);
