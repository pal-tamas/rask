namespace Rask.Wasm.Browser;

/// <summary>A reading of the current screen orientation.</summary>
/// <param name="Type">The orientation type.</param>
/// <param name="Angle">The clockwise angle in degrees relative to the natural orientation (0/90/180/270).</param>
public sealed record OrientationInfo(OrientationType Type, int Angle);
