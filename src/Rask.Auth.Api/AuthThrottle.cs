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

    /// <summary>Whether <paramref name="key" /> has used up its attempts.</summary>
    public bool IsThrottled(string key) =>
        _attempts.TryGetValue(key, out var attempts) && attempts.Count(clock.GetUtcNow(), Window) >= Limit;

    /// <summary>Records a failed attempt.</summary>
    public void Hit(string key)
    {
        var now = clock.GetUtcNow();
        _attempts.AddOrUpdate(key, _ => new Attempts(now), (_, a) => a.Add(now, Window));

        // Keep the table from growing without bound under a spray of distinct keys.
        if (_attempts.Count > 10_000)
        {
            foreach (var (k, a) in _attempts)
            {
                if (a.Count(now, Window) == 0)
                {
                    _attempts.TryRemove(k, out _);
                }
            }
        }
    }

    /// <summary>Forgets the failures of a key that has just succeeded.</summary>
    public void Clear(string key) => _attempts.TryRemove(key, out _);

    private sealed class Attempts
    {
        private readonly Queue<DateTimeOffset> _times = new();

        public Attempts(DateTimeOffset now) => _times.Enqueue(now);

        public Attempts Add(DateTimeOffset now, TimeSpan window)
        {
            lock (_times)
            {
                Trim(now, window);
                _times.Enqueue(now);
            }

            return this;
        }

        public int Count(DateTimeOffset now, TimeSpan window)
        {
            lock (_times)
            {
                Trim(now, window);
                return _times.Count;
            }
        }

        private void Trim(DateTimeOffset now, TimeSpan window)
        {
            while (_times.Count > 0 && now - _times.Peek() >= window)
            {
                _times.Dequeue();
            }
        }
    }
}
