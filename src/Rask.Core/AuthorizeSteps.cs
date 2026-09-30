using Rask.Core.Components;

namespace Rask;

/// <summary>
///     A role gate in words: <c>Authorize.Role("admin")[adminPanel]</c>, <c>Authorize.Roles("admin", "editor")[tools]</c>.
/// </summary>
public static class AuthorizeSteps
{
    extension(Authorize gate)
    {
        /// <summary>Passes a user in <paramref name="role" />.</summary>
        /// <param name="role">The role the gate accepts.</param>
        /// <returns>The gate.</returns>
        public Authorize Role(string role) => gate.Roles([role]);

        /// <summary>Passes a user in any one of these roles.</summary>
        /// <param name="role">A role the gate accepts.</param>
        /// <param name="more">The other roles it accepts.</param>
        /// <returns>The gate.</returns>
        public Authorize Roles(string role, params string[] more) => gate.Roles([role, .. more]);
    }
}
