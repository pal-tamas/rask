using System.Security.Claims;

namespace Rask.Core.Authentication;

/// <summary>Whether two principals are the same user with the same rights.</summary>
/// <remarks>
///     <para>
///         What a live session compares before it takes a principal as news: the one a revalidator handed back, the
///         one a reconnect carries, the one the browser's cookie names now. Every claim counts — the subject, a role,
///         a tenant, the id of the sign-in — except the ones a token carries about ITSELF, which change each time it
///         is issued again and say nothing about its bearer. A refreshed token is the same user; a flipped flag is not.
///     </para>
///     <para>
///         Allocates two enumerators and no strings, and nothing at all for one instance compared with itself.
///     </para>
/// </remarks>
internal static class PrincipalRights
{
    /// <summary>True when both are signed in, or both are not, and their claims say the same.</summary>
    internal static bool Same(ClaimsPrincipal left, ClaimsPrincipal right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return IsSignedIn(left) == IsSignedIn(right) && (SameInOrder(left, right) || SameInAnyOrder(left, right));
    }

    /// <summary>Whether <paramref name="principal" /> is somebody.</summary>
    internal static bool IsSignedIn(ClaimsPrincipal principal) => principal.Identity?.IsAuthenticated == true;

    // The usual case: two principals built by the same code from the same account list their claims alike.
    private static bool SameInOrder(ClaimsPrincipal left, ClaimsPrincipal right)
    {
        using var ours = left.Claims.GetEnumerator();
        using var theirs = right.Claims.GetEnumerator();

        while (true)
        {
            var more = NextThatCounts(ours);
            if (more != NextThatCounts(theirs))
            {
                return false;
            }

            if (!more)
            {
                return true;
            }

            if (!SameClaim(ours.Current, theirs.Current))
            {
                return false;
            }
        }
    }

    private static bool SameInAnyOrder(ClaimsPrincipal left, ClaimsPrincipal right)
    {
        foreach (var claim in left.Claims)
        {
            if (Counts(claim) && Occurrences(left, claim) != Occurrences(right, claim))
            {
                return false;
            }
        }

        return Counted(left) == Counted(right);
    }

    private static bool NextThatCounts(IEnumerator<Claim> claims)
    {
        while (claims.MoveNext())
        {
            if (Counts(claims.Current))
            {
                return true;
            }
        }

        return false;
    }

    private static int Occurrences(ClaimsPrincipal principal, Claim wanted)
    {
        var found = 0;
        foreach (var claim in principal.Claims)
        {
            if (SameClaim(claim, wanted))
            {
                found++;
            }
        }

        return found;
    }

    private static int Counted(ClaimsPrincipal principal)
    {
        var counted = 0;
        foreach (var claim in principal.Claims)
        {
            if (Counts(claim))
            {
                counted++;
            }
        }

        return counted;
    }

    private static bool SameClaim(Claim left, Claim right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal)
        && string.Equals(left.Value, right.Value, StringComparison.Ordinal);

    // What a token says about itself: when it was issued, when it lapses, which one it is. JWT's registered names,
    // and the two the legacy handler maps them to.
    private static bool Counts(Claim claim) => claim.Type switch
    {
        "exp" or "iat" or "nbf" or "jti" or "auth_time" or "nonce" or "at_hash" or "c_hash" => false,
        ClaimTypes.Expiration or ClaimTypes.AuthenticationInstant => false,
        _ => true,
    };
}
