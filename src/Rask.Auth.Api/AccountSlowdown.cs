using System.Collections.Concurrent;

namespace Rask.Auth;

/// <summary>
///     Failed sign-ins at one account, from anywhere: past a few, each next attempt waits longer before its password is
///     checked — 1 s, 2 s, 4 s… up to <see cref="Cap" />. A slowdown, never a lockout.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="AuthThrottle" /> counts an account and a client together, so a guesser spread over many addresses is
///         never slowed at any one account. This is the account's own count. It only ever makes an attempt wait: the owner,
///         typing the right password while somebody else guesses, gets in after at most <see cref="Cap" />, and their
///         success clears the count. Refusing instead would let anybody keep anybody out.
///     </para>
///     <para>In memory, per process, like the throttle: a second replica counts its own.</para>
/// </remarks>
internal sealed class AccountSlowdown(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Failures> _accounts = new(StringComparer.Ordinal);

    /// <summary>How many failures an account takes in <see cref="Window" /> before attempts at it wait.</summary>
    internal int Free { get; init; } = 5;

    /// <summary>The window failures are counted over.</summary>
    internal TimeSpan Window { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>The longest an attempt waits.</summary>
    internal TimeSpan Cap { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How many accounts the slowdown counts at most. Past it the stalest are let go of (<see cref="StalestKeys" />):
    ///     a spray of addresses that all failed just now costs the process no more than this.
    /// </summary>
    internal int MostKeys { get; init; } = 10_000;

    /// <summary>How many accounts the slowdown counts now.</summary>
    internal int KeyCount => _accounts.Count;

    /// <summary>How long an attempt at <paramref name="account" /> waits before its password is checked.</summary>
    public TimeSpan DelayFor(string account)
    {
        if (!_accounts.TryGetValue(account, out var failures))
        {
            return TimeSpan.Zero;
        }

        var over = failures.Count(clock.GetUtcNow(), Window) - Free;
        return over < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(Math.Pow(2, over), Cap.TotalSeconds));
    }

    public void Failed(string account)
    {
        var now = clock.GetUtcNow();
        Sweep(account, now);
        _accounts.GetOrAdd(account, static _ => new Failures()).Add(now);
    }

    public void Succeeded(string account) => _accounts.TryRemove(account, out _);

    // Keeps a spray of addresses from growing the table without bound: first the accounts whose failures have all
    // expired, then the stalest, down to nine tenths of the bound, so the next sweep is that many new accounts away.
    private void Sweep(string arriving, DateTimeOffset now)
    {
        if (_accounts.Count < MostKeys || _accounts.ContainsKey(arriving))
        {
            return;
        }

        foreach (var (account, failures) in _accounts)
        {
            if (failures.Count(now, Window) == 0)
            {
                _accounts.TryRemove(account, out _);
            }
        }

        foreach (var stalest in StalestKeys.Over(_accounts, MostKeys, static failures => failures.LastSeen))
        {
            _accounts.TryRemove(stalest);
        }
    }

    private sealed class Failures
    {
        private readonly Queue<DateTimeOffset> _at = new();
        private DateTimeOffset _lastSeen;

        // When the account last failed: what the stalest are let go of by.
        public DateTimeOffset LastSeen
        {
            get
            {
                lock (_at)
                {
                    return _lastSeen;
                }
            }
        }

        public void Add(DateTimeOffset at)
        {
            lock (_at)
            {
                _at.Enqueue(at);
                _lastSeen = at > _lastSeen ? at : _lastSeen;
            }
        }

        public int Count(DateTimeOffset now, TimeSpan window)
        {
            lock (_at)
            {
                while (_at.TryPeek(out var oldest) && now - oldest >= window)
                {
                    _at.Dequeue();
                }

                return _at.Count;
            }
        }
    }
}
