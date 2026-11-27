namespace Rask.Auth;

/// <summary>Whether visitors may create their own account (<see cref="AuthOptions.Registration" />).</summary>
public enum Registration
{
    /// <summary>Anybody may register. The default, so a fresh app works with no configuration.</summary>
    Open,

    /// <summary>
    /// Nobody registers themselves: an administrator creates accounts (<c>IAccounts.Create</c>). <c>POST
    /// /api/auth/register</c> is not mapped, so it answers what any unknown path does, and <c>IAuth.Register</c> answers
    /// <c>AuthError.NotAllowed</c>. The one registration still taken is the first-run claim of an instance with no
    /// accounts yet, through <c>IAuth.Register</c> with the first-run token, so a fresh deployment can make its
    /// administrator.
    /// </summary>
    Closed,
}
