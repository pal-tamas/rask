using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Rask.Auth;

/// <summary>
///     Issues the bearer tokens <see cref="AuthOptions.Bearer" /> turns on.
/// </summary>
/// <remarks>
///     <para>
///         Cookie is the default everywhere and stays that way. Bearer exists for the callers a cookie
///         cannot serve — a native client, a CLI, a service-to-service call — and is a configured
///         deviation, off unless an app asks for it.
///     </para>
///     <para>
///         <b>Access token only. There is no refresh token</b>, and that is a decision rather than an
///         omission: a refresh token needs a revocation story, revocation needs storage, and storage is
///         a much larger feature than the one being asked for. A short access token is honest about
///         what it is — when it expires the caller signs in again.
///     </para>
///     <para>
///         The signing key comes from configuration and from nowhere else. It cannot be generated at
///         startup: a generated key does not survive a restart and is not shared between instances, so
///         every token would die on deploy and nothing would work behind two replicas.
///     </para>
/// </remarks>
internal static class BearerTokens
{
    /// <summary>
    ///     The shortest key HMAC-SHA256 signing accepts, in bytes. A shorter one throws from inside the
    ///     token handler on the first sign-in rather than at startup, which is the wrong place to find out.
    /// </summary>
    internal const int MinimumKeyBytes = 32;

    internal static string Issue(ClaimsPrincipal principal, AuthOptions options, TimeProvider clock)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.BearerSigningKey!));
        var now = clock.GetUtcNow().UtcDateTime;

        var token = new JwtSecurityToken(
            options.BearerIssuer,
            options.BearerAudience,
            principal.Claims,
            now,
            now.Add(options.BearerLifetime),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    ///     Why the configured key cannot be used, or <see langword="null" /> when it can.
    /// </summary>
    internal static string? Reject(AuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BearerSigningKey))
        {
            return $"{nameof(AuthOptions)}.{nameof(AuthOptions.Bearer)} is on but "
                   + $"{nameof(AuthOptions.BearerSigningKey)} is empty. The key has to come from "
                   + "configuration: one generated at startup does not survive a restart and is not "
                   + "shared between instances, so every token would die on deploy and nothing would "
                   + "work behind two replicas.";
        }

        var bytes = Encoding.UTF8.GetByteCount(options.BearerSigningKey);
        return bytes < MinimumKeyBytes
            ? $"{nameof(AuthOptions)}.{nameof(AuthOptions.BearerSigningKey)} is {bytes} bytes; "
              + $"HMAC-SHA256 signing needs at least {MinimumKeyBytes}. A shorter key throws from inside "
              + "the token handler on the first sign-in, which is a far worse place to find out."
            : null;
    }
}
