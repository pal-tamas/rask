namespace Rask.Core.Browser;

/// <summary>The outcome of requesting access to a motion/orientation sensor.</summary>
public enum SensorPermissionState
{
    /// <summary>Access granted (or no prompt was required on this platform).</summary>
    Granted,

    /// <summary>Access denied by the user or the platform.</summary>
    Denied
}
