namespace Rask.Core.Browser;

/// <summary>Current state of a queried permission (<c>PermissionStatus.state</c>).</summary>
public enum PermissionState
{
    /// <summary>The user has not yet decided — the API will prompt on first use.</summary>
    Prompt,

    /// <summary>Access is granted; the feature can be used without prompting.</summary>
    Granted,

    /// <summary>Access is denied; the feature is blocked until the user changes the setting.</summary>
    Denied
}
