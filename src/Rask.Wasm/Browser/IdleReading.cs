namespace Rask.Wasm.Browser;

/// <summary>One idle-state change reported by the Idle Detection API.</summary>
/// <param name="UserIdle">
///     Whether the user is idle — no input and no screen interaction for the configured threshold
///     (<c>userState === "idle"</c>).
/// </param>
/// <param name="ScreenLocked">Whether the device screen is locked (<c>screenState === "locked"</c>).</param>
public sealed record IdleReading(bool UserIdle, bool ScreenLocked);
