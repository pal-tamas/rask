using System.Collections.Concurrent;
using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Data;

namespace Rask.Server;

/// <summary>
///     The answers the app's <see cref="ISessionRevalidator" /> gave, kept for <see cref="SessionUserKeeping.Keep" />
///     under the user's id and <see cref="PrincipalFingerprint" />, and dropped, all of them, the moment a save of a type
///     <c>.ForgetOnSave&lt;T&gt;()</c> named commits in this process.
/// </summary>
/// <remarks>
///     <para>
///         Two sessions share an answer only when their principals carry the same claims (<c>iat</c> and <c>nbf</c>
///         aside): another token expiry, another chosen tenant or another sign-in is asked for itself.
///     </para>
///     <para>
///         All of them are dropped, not the saved user's: a save says which types it wrote, not which rows. A load that
///         a save overtook — the save committed while the question was on its way — is answered and not kept. A save
///         in another process is seen when the kept answer runs out.
///     </para>
/// </remarks>
internal sealed class KeptSessionAnswers(SessionUserKeeping keeping, TimeProvider clock) : ISavedTypeWatcher
{
    // Past this many, a store also sweeps out what has run out: one entry per principal that used the app lately.
    private const int SweepAbove = 1024;

    private readonly ConcurrentDictionary<string, Kept> _kept = new(StringComparer.Ordinal);
    private long _forgotten;

    /// <summary><paramref name="app" />'s answer about <paramref name="principal" />, as kept or as it answers now.</summary>
    public async ValueTask<ClaimsPrincipal?> Answer(
        ClaimsPrincipal principal, ISessionRevalidator app, CancellationToken cancellationToken)
    {
        var keep = keeping.Keep;
        if (keep <= TimeSpan.Zero || RaskEndpointExtensions.UserKey(principal) is not { Length: > 0 } userId)
        {
            return await app.Revalidate(principal, cancellationToken).ConfigureAwait(false);
        }

        var key = userId + "\n" + PrincipalFingerprint.Of(principal);
        var now = clock.GetUtcNow();
        if (_kept.TryGetValue(key, out var kept) && now < kept.Until)
        {
            // "Unchanged" is handed back as the instance asked about, which costs the session nothing.
            return kept.Unchanged ? principal : kept.Answer;
        }

        var before = Interlocked.Read(ref _forgotten);
        var answer = await app.Revalidate(principal, cancellationToken).ConfigureAwait(false);
        if (Interlocked.Read(ref _forgotten) == before)
        {
            var unchanged = ReferenceEquals(answer, principal);
            var entry = new Kept(unchanged, unchanged ? null : answer, now + keep);
            _kept[key] = entry;

            // A save that committed between the check and the store has cleared already: take it back out.
            if (Interlocked.Read(ref _forgotten) != before)
            {
                _kept.TryRemove(new KeyValuePair<string, Kept>(key, entry));
            }

            SweepWhenLarge(now);
        }

        return answer;
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

        foreach (var (key, kept) in _kept)
        {
            if (now >= kept.Until)
            {
                _kept.TryRemove(new KeyValuePair<string, Kept>(key, kept));
            }
        }
    }

    // An unchanged answer keeps no principal: the next asker gets its own back.
    private sealed record Kept(bool Unchanged, ClaimsPrincipal? Answer, DateTimeOffset Until);
}
