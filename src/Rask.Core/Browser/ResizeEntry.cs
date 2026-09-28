namespace Rask.Core.Browser;

/// <summary>One size notification for an observed element (its content-box size, in CSS pixels).</summary>
/// <param name="Width">Content-box width.</param>
/// <param name="Height">Content-box height.</param>
public sealed record ResizeEntry(double Width, double Height);
