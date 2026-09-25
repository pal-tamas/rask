namespace Rask.Querying;

/// <summary>Where a renderable command is in its one-shot lifecycle.</summary>
public enum CommandStatus
{
    /// <summary>Never run, or reset.</summary>
    Idle,

    /// <summary>Dispatched and not yet answered. This is what disables the button.</summary>
    Pending,

    /// <summary>The last run threw.</summary>
    Error,

    /// <summary>The last run succeeded.</summary>
    Success,
}
