using System.ComponentModel.DataAnnotations;

namespace Company.RaskServer.Features.Shared;

// Your user. Add the columns your app needs — a locale, a team id — and create the migration with
// `rask db add AddUserColumns && rask db update`.
//
// Everything a sign-in needs comes from Authenticatable: the email, the password hash, the confirmation and
// the roles. It is an aggregate like any other, so read it with User.Read.Where(u => u.Id == id) and change it with
// User.Update(id, u => u.Rename(name)); register, sign in and reset passwords through Rask's IAuth.
public sealed class User : Authenticatable
{
    [MaxLength(100)]
    public string DisplayName { get; private set; } = "";

    public void Rename(string displayName) => DisplayName = displayName.Trim();
}
