namespace Rask.Server.Diagnostics;

/// <summary>How a connection came to a session, for the line that says it attached.</summary>
internal enum AttachKind
{
    /// <summary>The session's first connection, from the page its GET served.</summary>
    New,

    /// <summary>A reconnect to a session whose connection had gone.</summary>
    Resume,

    /// <summary>A reconnect that took over from a connection the server still thought was open.</summary>
    Replace,

    /// <summary>A session this host did not have, rebuilt from the tab's resume record.</summary>
    Rebuilt,
}
