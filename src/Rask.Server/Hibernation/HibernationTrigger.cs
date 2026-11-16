namespace Rask.Server.Hibernation;

/// <summary>What asked a session to hibernate.</summary>
internal enum HibernationTrigger
{
    /// <summary>Its tab has been out of sight for longer than the host keeps a hidden page awake.</summary>
    Hidden,

    /// <summary>The host is short of memory or of session slots, and this tab has been out of sight the longest.</summary>
    Pressure,
}
