namespace Rask.Wasm.Browser;

/// <summary>The result of showing the PWA install prompt.</summary>
public enum InstallOutcome
{
    /// <summary>The user accepted and the app is being installed.</summary>
    Accepted,

    /// <summary>The user dismissed the prompt.</summary>
    Dismissed,

    /// <summary>No install prompt was available to show (already installed, or not yet installable).</summary>
    Unavailable
}
