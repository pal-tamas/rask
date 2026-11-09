using System.Collections.Concurrent;
using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Data;

namespace Rask.Server;

/// <summary>
///     The answers the app's <see cref="ISessionRevalidator" /> gave, kept for <see cref="SessionUserKeeping.Keep" />
///     under the user's id and <see cref="PrincipalFingerprint" />; one user's dropped by <see cref="Forget(string)" />, and
///     all of them the moment a save of a type <c>.ForgetOnSave&lt;T&gt;()</c> named commits in this process.
/// </summary>
/// <remarks>
///     <para>
///         Two sessions share an answer only when their principals carry the same claims (<c>iat</c> and <c>nbf</c>
///         aside): another token expiry, another chosen tenant or another sign-in is asked for itself.
///     </para>
///     <para>
///         A save drops all of them, not the saved user's: a save says which types it wrote, not which rows. A save
///         in another process is seen when the kept answer runs out.
///     </para>
///     <para>
///         Each user's answers live in a <see cref="UserAnswers" /> of their own, taken BEFORE the app is asked. Dropping
///         one takes it out of the store, so an answer the app was giving meanwhile — read before the change it was
///         dropped for — lands in a set nothing reads any more: a forget or a save racing a question always wins.
///     </para>
/// </remarks>
internal sealed class KeptSessionAnswers(SessionUserKeeping keeping, TimeProvider clock) : ISavedTypeWatcher, ISessions
{
    // Past this many users, a store also sweeps out what has run out: one entry per user who used the app lately.
    private const int SweepAbove = 1024;

    private readonly ConcurrentDictionary<string, UserAnswers> _kept = new(StringComparer.Ordinal);

    /// <summary><paramref name="app" />'s answer about <paramref name="principal" />, as kept or as it answers now.</summary>
    public async ValueTask<ClaimsPrincipal?> Answer(
        ClaimsPrincipal principal, ISessionRevalidator app, CancellationToken cancellationToken)
    {
        var keep = keeping.Keep;
        if (keep <= TimeSpan.Zero || RaskEndpointExtensions.UserKey(principal) is not { Length: > 0 } userId)
        {
            return await app.Revalidate(principal, cancellationToken).ConfigureAwait(false);
        }

        var fingerprint = PrincipalFingerprint.Of(principal);
        var now = clock.GetUtcNow();
        var answers = _kept.GetOrAdd(userId, static _ => new UserAnswers());
        if (answers.TryGetValue(fingerprint, out var kept) && now < kept.Until)
        {
            // "Unchanged" is handed back as the instance asked about, which costs the session nothing.
            return kept.Unchanged ? principal : kept.Answer;
        }

        var answer = await app.Revalidate(principal, cancellationToken).ConfigureAwait(false);
        var unchanged = ReferenceEquals(answer, principal);
        answers[fingerprint] = new Kept(unchanged, unchanged ? null : answer, now + keep);
        SweepWhenLarge(now);
        return answer;
    }

    /// <inheritdoc />
    public void Forget(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        _kept.TryRemove(userId, out _);
    }

    /// <inheritdoc />
    public void Forget(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (RaskEndpointExtensions.UserKey(user) is { Length: > 0 } userId)
        {
            _kept.TryRemove(userId, out _);
        }
    }

    /// <inheritdoc />
    public bool Watches(Type aggregate) => keeping.ForgetsOnSaveOf(aggregate);

    /// <inheritdoc />
    public void Saved(IReadOnlyCollection<Type> aggregates) => _kept.Clear();

    private void SweepWhenLarge(DateTimeOffset now)
    {
        if (_kept.Count <= SweepAbove)
        {
            return;
        }

        foreach (var (userId, answers) in _kept)
        {
            foreach (var (fingerprint, kept) in answers)
            {
                if (now >= kept.Until)
                {
                    answers.TryRemove(new KeyValuePair<string, Kept>(fingerprint, kept));
                }
            }

            // An answer stored into it meanwhile goes with it: a question asked again, never a stale answer.
            if (answers.IsEmpty)
            {
                _kept.TryRemove(new KeyValuePair<string, UserAnswers>(userId, answers));
            }
        }
    }

    // An unchanged answer keeps no principal: the next asker gets its own back.
    private sealed record Kept(bool Unchanged, ClaimsPrincipal? Answer, DateTimeOffset Until);

    // One user's answers, one per principal fingerprint. Its identity is what a forget invalidates.
    private sealed class UserAnswers() : ConcurrentDictionary<string, Kept>(StringComparer.Ordinal);
}
