using System.Globalization;
using System.Security.Claims;

namespace Rask.Server.Authentication;

/// <summary>
///     When a principal's sign-in lapses by its own word: the earliest <c>exp</c> claim it carries (RFC 7519, seconds
///     since the Unix epoch), plus <see cref="ClockSkew" />. Null for a principal that carries none, which lapses never.
/// </summary>
/// <remarks>
///     Worked out once, as the session takes the principal, so an event compares two timestamps and allocates nothing.
/// </remarks>
internal static class SignInExpiry
{
    /// <summary>The claim a token's expiry travels in.</summary>
    public const string Claim = "exp";

    /// <summary>
    ///     How long past its <c>exp</c> a sign-in is still taken: the five minutes ASP.NET's own JWT validation allows
    ///     (TokenValidationParameters.DefaultClockSkew), so a live page never signs out a reader whose next request the
    ///     app's authentication would still let in, only to be sent straight back.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    // DateTimeOffset's own range, in Unix seconds: a value past it is no date, and is not taken as one.
    private static readonly long MinSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxSeconds = DateTimeOffset.MaxValue.Subtract(ClockSkew).ToUnixTimeSeconds();

    /// <summary>The moment <paramref name="principal" />'s sign-in lapses, skew included; null when it carries no expiry.</summary>
    public static DateTimeOffset? LapsesAt(ClaimsPrincipal principal)
    {
        DateTimeOffset? earliest = null;
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.Claims)
            {
                // A NumericDate may carry a fraction (RFC 7519 §2); the whole second before it is the one taken.
                if (string.Equals(claim.Type, Claim, StringComparison.Ordinal)
                    && double.TryParse(claim.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    && seconds >= MinSeconds && seconds <= MaxSeconds)
                {
                    var lapses = DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(seconds)) + ClockSkew;
                    earliest = earliest is { } sooner && sooner <= lapses ? sooner : lapses;
                }
            }
        }

        return earliest;
    }

    /// <summary>Whether a sign-in that lapses at <paramref name="lapsesAt" /> has lapsed by <paramref name="clock" />.</summary>
    public static bool HasLapsed(DateTimeOffset? lapsesAt, TimeProvider clock) =>
        lapsesAt is { } at && clock.GetUtcNow() >= at;
}
