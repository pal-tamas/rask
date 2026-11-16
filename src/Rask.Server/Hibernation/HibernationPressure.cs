namespace Rask.Server.Hibernation;

/// <summary>Whether the host is short of memory, and how many sessions it holds over its target.</summary>
/// <param name="Memory">The memory in use is past <see cref="SessionHibernationOptions.MemoryLoadLimit" />.</param>
/// <param name="OverSessions">Sessions held over <see cref="SessionHibernationOptions.SessionsTarget" /> of the cap.</param>
internal readonly record struct HibernationPressure(bool Memory, int OverSessions)
{
    /// <summary>Whether anything presses.</summary>
    public bool Any => Memory || OverSessions > 0;

    /// <summary>Reads the pressure from the memory in use and the store's slots.</summary>
    public static HibernationPressure Read(
        MemoryLoad memory, int liveSessions, int maxSessions, SessionHibernationOptions options)
    {
        var memoryHigh = memory.AvailableBytes > 0 && memory.InUseBytes >= memory.AvailableBytes * options.MemoryLoadLimit;

        var over = 0;
        if (maxSessions > 0 && liveSessions >= maxSessions * options.SessionsLimit)
        {
            over = Math.Max(1, liveSessions - (int)(maxSessions * options.SessionsTarget));
        }

        return new HibernationPressure(memoryHigh, over);
    }
}
