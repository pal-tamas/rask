using Rask.Data;

namespace Rask.Auth;

/// <summary>
/// One signed-in device: the row a sign-in cookie points at.
/// </summary>
/// <remarks>
/// <para>
/// The cookie carries only this row's id, signed. Every request and every live-socket attach loads the row, so ending a
/// session is deleting it: signing out, <c>IAuth.SignOutOtherDevicesAsync</c>, and a password reset all take effect on the
/// device's next request instead of when its cookie happens to expire.
/// </para>
/// <para>
/// Read it like any aggregate to list a user's devices: <c>Session.Read.Where(s =&gt; s.UserId == id).ToListAsync()</c>. Rask.Auth
/// creates and ends sessions; an app does not.
/// </para>
/// </remarks>
public sealed class Session : Aggregate<Guid>
{
    /// <summary>
    ///     Ending a session KEEPS the row: the devices list shows it, and an audit of who signed in from
    ///     where would be worthless if signing out erased the evidence. Soft delete is opt-in now, so this
    ///     says so.
    /// </summary>
    public const Deletion Deletes = Deletion.Soft;

    /// <summary>The signed-in user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The address the sign-in came from, when it was known.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>The browser's user-agent string at sign-in, when it sent one.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>When this device was last seen, to the minute.</summary>
    public DateTime LastSeenAt { get; private set; }

    /// <summary>When the session ends unless it is used again first.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Whether the sign-in asked to be remembered, so the cookie outlives the browser.</summary>
    public bool Persistent { get; private set; }

    /// <summary>The longest a user agent is kept, in characters.</summary>
    internal const int UserAgentLength = 512;

    /// <summary>The longest an address is kept, in characters.</summary>
    internal const int IpAddressLength = 64;

    internal static Session Start(
        Guid userId, string? ipAddress, string? userAgent, bool persistent, DateTime now, TimeSpan lifetime)
    {
        var session = new Session
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            IpAddress = Truncate(ipAddress, IpAddressLength),
            UserAgent = Truncate(userAgent, UserAgentLength),
            Persistent = persistent,
            LastSeenAt = now,
            ExpiresAt = now + lifetime,
        };

        session.Raise(new SignedIn(userId, session.Id));
        return session;
    }

    internal bool IsActive(DateTime now) => ExpiresAt > now;

    // Written at most once a minute, so an active page does not turn every request into a write.
    internal bool NeedsTouch(DateTime now) => now - LastSeenAt >= TimeSpan.FromMinutes(1);

    internal void Touch(DateTime now, TimeSpan lifetime, bool sliding)
    {
        LastSeenAt = now;

        if (sliding)
        {
            ExpiresAt = now + lifetime;
        }
    }

    internal void End(DateTime now)
    {
        ExpiresAt = now;
        Raise(new SignedOut(UserId, Id));
    }

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
