using Microsoft.AspNetCore.Identity;

namespace Company.RaskServer.Features.Shared;

// Your account. Add the columns your app needs — a display name, a locale, a team id — and create
// the migration with `rask db add AddUserColumns && rask db update`.
//
// Everything an account already has comes from Identity: the password hash, the security stamp,
// the lockout counters and the confirmation flags. Inject UserManager<User> to work with
// accounts, or Rask's IAuth for the surface that works on every host.
//
// Add `, ITimestamped` to get CreatedAt/UpdatedAt columns without declaring either — Rask stamps
// them. Do NOT add IVersioned: Identity already maintains ConcurrencyStamp, and a second token on
// the same row is a race rather than a guard.
public class User : IdentityUser
{
}