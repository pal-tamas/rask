using Rask.Values;

namespace Company.RaskServer.Features.Shared;

// Your user. Add the columns your app needs — a locale, a team id — and create the migration with
// `rask db add AddUserColumns`; the app applies it when it starts.
//
// Everything a sign-in needs comes from Authenticatable: the email, the password hash, the confirmation and
// the roles. It is an aggregate like any other, so read it with User.Where(u => u.Id == id) and change it with
// User.Update(id, u => u.Rename(name)); register, sign in and reset passwords through Rask's IAuth.
public sealed class User : Authenticatable
{
    // None until the person gives one: an account an administrator invites is made without it.
    public UserDisplayName? DisplayName { get; private set; }

    public void Rename(string displayName) => DisplayName = UserDisplayName.CreateOptional(displayName, nameof(DisplayName));
}

// The name a user is shown by. Its rules are checked wherever one is made — the register form asks the same.
public sealed partial record UserDisplayName(string Value) : Text(Value, MaxLength: MaxLength, Required: false)
{
    public const int MaxLength = 100;
}
