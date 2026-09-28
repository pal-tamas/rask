namespace Rask.DevTools;

/// <summary>
///     Present in a container exactly when the devtools attached to it. The feed, the probe and the panel
///     hang off this registration in later slices; on its own it is inert.
/// </summary>
internal sealed class DevToolsRegistration
{
    /// <summary>The one registration; it carries nothing, so every container can share it.</summary>
    internal static readonly DevToolsRegistration Instance = new();

    private DevToolsRegistration()
    {
    }
}
