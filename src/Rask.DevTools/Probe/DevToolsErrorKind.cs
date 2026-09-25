namespace Rask.DevTools.Probe;

/// <summary>Where an error the Errors tab lists came from.</summary>
internal enum DevToolsErrorKind : byte
{
    /// <summary>A component's <c>Render()</c> threw.</summary>
    Render,

    /// <summary>An event handler threw.</summary>
    Handler,

    /// <summary>An async lifecycle hook faulted.</summary>
    Lifecycle,

    /// <summary>The framework reported a warning or an error through its diagnostics.</summary>
    Diagnostic,

    /// <summary>A script on the page threw, or a promise was rejected with nobody to catch it.</summary>
    Page,

    /// <summary>An island failed to mount, update or unmount.</summary>
    Island,
}
