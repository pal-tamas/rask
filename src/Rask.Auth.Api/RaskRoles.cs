namespace Rask.Auth;

/// <summary>The roles a Rask app has out of the box.</summary>
/// <remarks>
/// Two, deliberately: the first account to register is an <see cref="Admin"/> and every account after it is a
/// <see cref="User"/>. Anything richer is the app's own: a role is just a name a user holds, so granting a new one is
/// <c>User.Update(id, u =&gt; u.GrantRole("editor"))</c>.
/// </remarks>
public static class RaskRoles
{
    /// <summary>The administrator role. Held by the first account to register, and gates <c>/_rask</c>.</summary>
    public const string Admin = "admin";

    /// <summary>The ordinary signed-in role. Held by every account after the first.</summary>
    public const string User = "user";

    /// <summary>Both roles.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, User];
}
