using System.Collections.Concurrent;

namespace Rask.Auth;

/// <summary>
/// Too many attempts from one address at one account: the answer is "wait a minute", never "this account is locked".
/// </summary>
/// <remarks>
/// <para>
/// Laravel's and Rails' shape. Locking the account after five failures (what Identity did) lets anybody lock anybody out by
/// typing their address five times. Throttling the address-and-client pair slows a guesser to a crawl and leaves the owner,
/// on their own device, free to sign in.
/// </para>
/// <para>
/// A sliding window per key, counted on failures and cleared by a success. In-memory, per process: a second replica has its
/// own counts, which is a weaker limit but never a lockout.
/// </para>
/// </remarks>
internal sealed class AuthThrottle(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Attempts> _attempts = new(StringComparer.Ordinal);

    /// <summary>How many failures a key may have in <see cref="Window" />.</summary>
    internal int Limit { get; init; } = 5;

    /// <summary>The window failures are counted over.</summary>
    internal TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The key for an attempt at <paramref name="email" /> from <paramref name="client" />.</summary>
    public static string Key(string purpose, string email, string? client) =>
        purpose + "|" + Authenticatable.NormalizeEmail(email) + "|" + (client ?? "-");

    /// <summary>
    /// Starts an attempt on <paramref name="key" />: checks the limit and, when there is room, counts the attempt — in one
    /// step, under the key's lock.
    /// </summary>
    /// <remarks>
    /// One step, not a check followed later by a count (#1121). Checked and counted separately, guesses fired together all
    /// passed the check before any of them was counted, so a guesser who parallelised got as many tries as they liked. The
    /// attempt therefore counts from the moment it begins; disposing it hands it back unless <see cref="Attempt.Fail" /> kept it,
    /// and <see cref="Attempt.Succeed" /> clears the key.
    /// </remarks>
    public Attempt Begin(string key) => Start(key, reserve: true);

    /// <summary>
    /// Starts an attempt on <paramref name="key" /> that counts only if it fails — for a key shared by many honest callers.
    /// </summary>
    /// <remarks>
    /// Registration's key is the client alone, so an office behind one address registering together would throttle
    /// itself if attempts counted while they ran. The price is that failures fired together can pass the check before any
    /// of them is counted; a key that names an account uses <see cref="Begin" />.
    /// </remarks>
    public Attempt Check(string key) => Start(key, reserve: false);

    private Attempt Start(string key, bool reserve)
    {
        var now = clock.GetUtcNow();
        Sweep(now);

        while (true)
        {
            var attempts = _attempts.GetOrAdd(key, static _ => new Attempts());
            switch (attempts.TryReserve(now, Window, Limit, reserve, out var stamp))
            {
                case true:
                    return new Attempt(this, key, attempts, stamp, throttled: false, now);
                case false:
                    return new Attempt(this, key, attempts, null, throttled: true, now);
            }

            // Retired by a sweep between the lookup and the lock: it is on its way out of the table, so take the next one.
        }
    }

    // Counts a failure that was not reserved up front, looking past an entry a sweep retired in the meantime.
    private void Record(string key, DateTimeOffset at)
    {
        while (!_attempts.GetOrAdd(key, static _ => new Attempts()).TryAdd(new Stamp(at, failed: true)))
        {
        }
    }

    // Keeps the table from growing without bound under a spray of distinct keys. An entry is retired under its own lock and
    // only while empty, and a retired entry refuses every reservation, so a count can never land on an entry that has
    // already left the table.
    private void Sweep(DateTimeOffset now)
    {
        if (_attempts.Count <= 10_000)
        {
            return;
        }

        foreach (var entry in _attempts)
        {
            if (entry.Value.TryRetire(now, Window))
            {
                _attempts.TryRemove(entry);
            }
        }
    }

    /// <summary>
    /// One attempt at a key. While it is open it counts against the key, so attempts running together see each other;
    /// disposing it hands it back unless <see cref="Fail" /> kept it.
    /// </summary>
    internal sealed class Attempt : IDisposable
    {
        private readonly AuthThrottle _owner;
        private readonly string _key;
        private readonly Attempts _attempts;
        private readonly DateTimeOffset _at;
        private Stamp? _stamp;
        private bool _settled;

        internal Attempt(AuthThrottle owner, string key, Attempts attempts, Stamp? stamp, bool throttled, DateTimeOffset at)
        {
            _owner = owner;
            _key = key;
            _attempts = attempts;
            _stamp = stamp;
            _at = at;
            IsThrottled = throttled;
            _settled = throttled;
        }

        /// <summary>The key had used up its attempts; nothing was counted.</summary>
        public bool IsThrottled { get; }

        /// <summary>Keeps the attempt counted — or, for one from <see cref="Check" />, counts it now: it failed.</summary>
        public void Fail()
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            if (_stamp is null)
            {
                _owner.Record(_key, _at);
            }
            else
            {
                _attempts.MarkFailed(_stamp);
            }

            _stamp = null;
        }

        /// <summary>
        /// Forgets the key's failures, which a success just outweighed. Attempts still running beside it keep their
        /// place: a guess that fails after the owner signed in counts like any other.
        /// </summary>
        public void Succeed()
        {
            _settled = true;
            if (_stamp is { } stamp)
            {
                _stamp = null;
                _attempts.Release(stamp);
            }

            _attempts.ForgetFailures();
        }

        /// <summary>Hands the attempt back unless it failed — a malformed request, a policy refusal and a crash are not guesses.</summary>
        public void Dispose()
        {
            _settled = true;
            if (_stamp is { } stamp)
            {
                _stamp = null;
                _attempts.Release(stamp);
            }
        }
    }

    internal sealed class Stamp(DateTimeOffset at, bool failed)
    {
        public DateTimeOffset At { get; } = at;

        // Pending while its attempt runs; failed once it is known to be a guess. Only failures are forgotten by a
        // success. Written under the owning list's lock.
        public bool Failed { get; set; } = failed;
    }

    internal sealed class Attempts
    {
        // Stamps, not timestamps: an attempt is released by identity, so two attempts in the same tick stay distinct.
        private readonly List<Stamp> _stamps = [];
        private bool _retired;

        // null: retired, the caller must look again. false: throttled. true: allowed (and counted, when reserving).
        public bool? TryReserve(DateTimeOffset now, TimeSpan window, int limit, bool reserve, out Stamp? stamp)
        {
            lock (_stamps)
            {
                stamp = null;
                if (_retired)
                {
                    return null;
                }

                Trim(now, window);
                if (_stamps.Count >= limit)
                {
                    return false;
                }

                if (reserve)
                {
                    stamp = new Stamp(now, failed: false);
                    _stamps.Add(stamp);
                }

                return true;
            }
        }

        // False when retired: the entry has left the table and a count landed on it would be lost.
        public bool TryAdd(Stamp stamp)
        {
            lock (_stamps)
            {
                if (_retired)
                {
                    return false;
                }

                _stamps.Add(stamp);
                return true;
            }
        }

        public void Release(Stamp stamp)
        {
            lock (_stamps)
            {
                _stamps.Remove(stamp);
            }
        }

        public void MarkFailed(Stamp stamp)
        {
            lock (_stamps)
            {
                stamp.Failed = true;
            }
        }

        public void ForgetFailures()
        {
            lock (_stamps)
            {
                _stamps.RemoveAll(static stamp => stamp.Failed);
            }
        }

        public bool TryRetire(DateTimeOffset now, TimeSpan window)
        {
            lock (_stamps)
            {
                Trim(now, window);
                _retired = _stamps.Count == 0;
                return _retired;
            }
        }

        private void Trim(DateTimeOffset now, TimeSpan window)
        {
            var expired = 0;
            while (expired < _stamps.Count && now - _stamps[expired].At >= window)
            {
                expired++;
            }

            _stamps.RemoveRange(0, expired);
        }
    }
}
