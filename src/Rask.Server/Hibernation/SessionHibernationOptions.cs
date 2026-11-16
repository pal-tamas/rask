namespace Rask.Server.Hibernation;

/// <summary>
///     When a host lets a hidden tab's session go into the tab's resume record. Internal: these are the values every
///     host runs with, and a test registers an instance of its own to make them short. None of it is a setting.
/// </summary>
internal sealed class SessionHibernationOptions
{
    /// <summary>How long a tab is out of sight before its session is asked to hibernate. Five minutes.</summary>
    public TimeSpan HiddenFor { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a tab is out of sight before a host short of memory or slots may ask it sooner.</summary>
    public TimeSpan HiddenUnderPressureFor { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the host looks for sessions to ask.</summary>
    public TimeSpan Every { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long after an offer, or a session that was not ready, the host waits before asking again.</summary>
    public TimeSpan AskAgainAfter { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The share of the memory available to the process in use from which the host is short of it.</summary>
    public double MemoryLoadLimit { get; init; } = 0.85;

    /// <summary>The share of <c>MaxSessions</c> held from which the host is short of slots.</summary>
    public double SessionsLimit { get; init; } = 0.9;

    /// <summary>The share of <c>MaxSessions</c> the host asks hidden tabs to bring it back down to.</summary>
    public double SessionsTarget { get; init; } = 0.8;
}
