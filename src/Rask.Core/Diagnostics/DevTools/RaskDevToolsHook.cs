namespace Rask.Core.Diagnostics.DevTools;

/// <summary>The one place the runtime looks for an attached probe.</summary>
internal static class RaskDevToolsHook
{
    /// <summary>Set by <c>Rask.DevTools</c> when it activates; null otherwise.</summary>
    internal static IRaskDevToolsProbe? Probe { get; set; }

    /// <summary>
    ///     The probe to call, or null. Reads the feature switch first, so a build without the devtools folds
    ///     this to null and every call site's branch disappears with it.
    /// </summary>
    internal static IRaskDevToolsProbe? Active => RaskDevToolsFeature.IsEnabled ? Probe : null;
}
