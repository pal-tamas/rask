using System.Collections.Concurrent;
using Rask.Data;

namespace Rask.Server;

/// <summary>
///     The users a <see cref="SessionRevalidator{TUser}" /> loaded, kept for <see cref="SessionUserKeeping.Keep" />
///     and shared by every session of each — and dropped, all of them, the moment a save of a type
///     <c>.ForgetOnSave&lt;T&gt;()</c> named commits in this process.
/// </summary>
/// <remarks>
///     <para>
///         All of them, not the saved one: a save says which types it wrote, not which rows. Saving a user is rare
///         and what it costs is one load per signed-in user at their next event.
///     </para>
///     <para>
///         A load that a save overtook — the save committed while the read was on its way, so what it read may be
///         the row from before — is answered and not kept. A save in another process is seen when the kept user
///         runs out.
///     </para>
/// </remarks>
internal sealed class KeptSessionUsers(SessionUserKeeping keeping, TimeProvider clock) : ISavedTypeWatcher
{
    // Past this many, a store also sweeps out what has run out: one entry per user who used the app lately.
    private const int SweepAbove = 1024;

    private readonly ConcurrentDictionary<string, Kept> _kept = new(StringComparer.Ordinal);
    private long _forgotten;

    /// <summary>The user <paramref name="userId" /> as kept, or as <paramref name="source" /> loads them now.</summary>
    public async Task<object?> Get(string userId, SessionRevalidator source, CancellationToken cancellationToken)
    {
        var keep = keeping.Keep;
        if (keep <= TimeSpan.Zero)
        {
            return await source.LoadUser(userId, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.GetUtcNow();
        if (_kept.TryGetValue(userId, out var kept) && now < kept.Until)
        {
            return kept.User;
        }

        var before = Interlocked.Read(ref _forgotten);
        var user = await source.LoadUser(userId, cancellationToken).ConfigureAwait(false);
        if (Interlocked.Read(ref _forgotten) == before)
        {
            var entry = new Kept(user, now + keep);
            _kept[userId] = entry;

            // A save that committed between the check and the store has cleared already: take it back out.
            if (Interlocked.Read(ref _forgotten) != before)
            {
                _kept.TryRemove(new KeyValuePair<string, Kept>(userId, entry));
            }

            SweepWhenLarge(now);
        }

        return user;
    }

    /// <inheritdoc />
    public bool Watches(Type aggregate) => keeping.ForgetsOnSaveOf(aggregate);

    /// <inheritdoc />
    public void Saved(IReadOnlyCollection<Type> aggregates)
    {
        Interlocked.Increment(ref _forgotten);
        _kept.Clear();
    }

    private void SweepWhenLarge(DateTimeOffset now)
    {
        if (_kept.Count <= SweepAbove)
        {
            return;
        }

        foreach (var (userId, kept) in _kept)
        {
            if (now >= kept.Until)
            {
                _kept.TryRemove(new KeyValuePair<string, Kept>(userId, kept));
            }
        }
    }

    private sealed record Kept(object? User, DateTimeOffset Until);
}
