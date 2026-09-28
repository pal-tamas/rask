namespace Rask.Core.Browser;

/// <summary>One device-orientation reading (a <c>deviceorientation</c> event).</summary>
/// <param name="Alpha">Rotation around the Z axis, 0–360° (compass heading); <c>null</c> if unavailable.</param>
/// <param name="Beta">Front-to-back tilt, -180–180°; <c>null</c> if unavailable.</param>
/// <param name="Gamma">Left-to-right tilt, -90–90°; <c>null</c> if unavailable.</param>
/// <param name="Absolute">Whether the reading is relative to the Earth's frame (true) or arbitrary (false).</param>
public sealed record OrientationReading(double? Alpha, double? Beta, double? Gamma, bool Absolute);
