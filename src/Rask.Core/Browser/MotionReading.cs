namespace Rask.Core.Browser;

/// <summary>One device-motion reading (a <c>devicemotion</c> event). Values are <c>null</c> when the device
/// can't report them. Acceleration is in m/s² (excluding gravity); rotation rate is in °/s.</summary>
/// <param name="AccelerationX">Acceleration along the X axis (m/s²).</param>
/// <param name="AccelerationY">Acceleration along the Y axis (m/s²).</param>
/// <param name="AccelerationZ">Acceleration along the Z axis (m/s²).</param>
/// <param name="RotationAlpha">Rotation rate around the Z axis (°/s).</param>
/// <param name="RotationBeta">Rotation rate around the X axis (°/s).</param>
/// <param name="RotationGamma">Rotation rate around the Y axis (°/s).</param>
/// <param name="Interval">Interval, in ms, at which data is obtained from the hardware.</param>
public sealed record MotionReading(
    double? AccelerationX,
    double? AccelerationY,
    double? AccelerationZ,
    double? RotationAlpha,
    double? RotationBeta,
    double? RotationGamma,
    double? Interval);
