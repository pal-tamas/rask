namespace Rask.Wasm.Browser;

/// <summary>What to capture in a <see cref="IMediaDevices.GetUserMediaAsync" /> request.</summary>
/// <param name="Video">Capture the camera.</param>
/// <param name="Audio">Capture the microphone.</param>
/// <param name="FacingMode">
///     Preferred camera when <paramref name="Video" /> is set — <c>"user"</c> (front) or
///     <c>"environment"</c> (rear). Ignored on devices with one camera.
/// </param>
public sealed record MediaConstraints(bool Video = true, bool Audio = false, string? FacingMode = null);
