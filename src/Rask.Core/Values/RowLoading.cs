namespace Rask.Values;

/// <summary>
///     Whether the value objects made right now are being loaded from the database, so their rules are not asked:
///     a row is what was stored, even when a rule has been tightened since.
/// </summary>
/// <remarks>
///     <para>
///         Two switches. The per-thread depth covers a synchronous stretch — EF Core materializing one entity
///         (Rask.Data's interceptor, between <c>CreatingInstance</c> and
///         <c>InitializedInstance</c>), a column's converter rebuilding one value — and costs nothing per row. The
///         flowing flag covers one of Rask's own reads from start to end (<see cref="Read" />), so a projection that
///         builds a value object outside any entity is loaded the same way.
///     </para>
///     <para>
///         A materialization that throws between the two interceptor calls would leave the depth up on its thread.
///         EF Core reports the failure as it happens, and Rask.Data's watch on failed materializations puts the depth back
///         to zero there; every read Rask runs also ends with <see cref="ReadScope.Dispose" />, which does the same —
///         so the next <c>Create</c> on that thread checks its rules again.
///     </para>
/// </remarks>
internal static class RowLoading
{
    [ThreadStatic]
    private static int t_depth;

    private static readonly AsyncLocal<bool> Reading = new();

    /// <summary>Whether a value object made now is being loaded.</summary>
    public static bool IsOn => t_depth > 0 || Reading.Value;

    /// <summary>Loads on this thread until the scope is disposed, on this thread, which restores the depth before.</summary>
    /// <returns>The scope to dispose.</returns>
    public static Scope Begin()
    {
        var previous = t_depth;
        t_depth = previous + 1;
        return new Scope(previous);
    }

    /// <summary>One entity's materialization starts: what EF Core builds for it now is loaded.</summary>
    public static void Enter() => t_depth++;

    /// <summary>One entity's materialization ends.</summary>
    public static void Leave()
    {
        if (t_depth > 0)
        {
            t_depth--;
        }
    }

    /// <summary>A materialization on this thread failed: whatever it began is over (Rask.Data's watch on failed materializations).</summary>
    public static void Abandon() => t_depth = 0;

    /// <summary>One of Rask's reads, from its start to its end — awaits included.</summary>
    /// <returns>The scope to dispose when the read is done, or has failed.</returns>
    public static ReadScope Read()
    {
        var previous = Reading.Value;
        Reading.Value = true;
        return new ReadScope(previous);
    }

    /// <summary>Restores the depth <see cref="Begin" /> found.</summary>
    internal readonly struct Scope(int previous) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => Restore(previous);

        private static void Restore(int depth) => t_depth = depth;
    }

    /// <summary>Ends a read: the flag as it was, and no materialization left open on this thread.</summary>
    internal readonly struct ReadScope(bool previous) : IDisposable
    {
        /// <inheritdoc />
        /// <remarks>
        ///     A read's end never runs inside a materialization, so a depth still up here can only be one a failed
        ///     materialization left behind — on this thread, which is the one EF Core's failed read resumes on.
        /// </remarks>
        public void Dispose() => End(previous);

        private static void End(bool previous)
        {
            Reading.Value = previous;
            t_depth = 0;
        }
    }
}
