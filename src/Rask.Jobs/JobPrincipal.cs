using System.Buffers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Rask.Data;

namespace Rask.Background;

/// <summary>
/// Who a job runs as: the user who enqueued it, with the rights they had THEN — never one they gained since, and
/// never one they have lost since.
/// </summary>
/// <remarks>
/// At enqueue the user's rights (roles and every claim that is not who they are) are written on the job's row.
/// At run time the principal is rebuilt from their account as it is now, and only the rights present in both
/// remain. A user whose account is gone runs as nobody. An app without the auth battery has no account to ask,
/// so its jobs run with the recorded rights alone — never more than the enqueuer had.
/// </remarks>
internal static class JobPrincipal
{
    private const string AuthenticationType = "Rask.Jobs";

    // Who the user is, not what they may do: taken from the account (or the job), never intersected.
    private static readonly HashSet<string> Identity = new(StringComparer.Ordinal)
    {
        ClaimTypes.NameIdentifier, ClaimTypes.Name, ClaimTypes.Email, "sid", Tenant.ClaimType,
    };

    // The tenant is the job's (it ran under Tenant.Use of it) and a session id names a sign-in the job is not.
    private static readonly HashSet<string> FromTheJob = new(StringComparer.Ordinal) { Tenant.ClaimType, "sid" };

    /// <summary>The rights of <paramref name="principal"/> as the job's row keeps them, or null when it has none.</summary>
    /// <remarks>
    /// Only when the principal IS <paramref name="userId"/>: a job enqueued under <c>Current.UseUser(other)</c>
    /// runs for that other user, and must not carry the signed-in user's rights to them.
    /// </remarks>
    public static string? Record(ClaimsPrincipal? principal, Guid userId)
    {
        if (!Guid.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var whose) || whose != userId)
        {
            return null;
        }

        var rights = principal.Claims.Where(claim => !Identity.Contains(claim.Type)).ToList();
        if (rights.Count == 0)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var claim in rights)
            {
                json.WriteStartArray();
                json.WriteStringValue(claim.Type);
                json.WriteStringValue(claim.Value);
                json.WriteEndArray();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The principal <paramref name="job"/> runs as, or null when it was enqueued by nobody.</summary>
    public static async Task<ClaimsPrincipal?> For(Job job, IAccountPrincipals? accounts, CancellationToken cancellationToken)
    {
        if (job.UserId is not { } userId)
        {
            return null;
        }

        var recorded = Read(job.UserClaims);
        if (accounts is null)
        {
            return Compose([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], recorded, job.TenantId);
        }

        if (await accounts.Of(userId, cancellationToken).ConfigureAwait(false) is not { } now)
        {
            return null;
        }

        var identity = now.Claims.Where(claim => Identity.Contains(claim.Type) && !FromTheJob.Contains(claim.Type));
        var kept = now.Claims.Where(claim => !Identity.Contains(claim.Type) && recorded.Contains((claim.Type, claim.Value)));
        return Compose(identity, kept.Select(claim => (claim.Type, claim.Value)).ToHashSet(), job.TenantId);
    }

    private static ClaimsPrincipal Compose(IEnumerable<Claim> identity, IEnumerable<(string Type, string Value)> rights, Guid? tenant)
    {
        var claims = new ClaimsIdentity(AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        claims.AddClaims(identity.Select(claim => new Claim(claim.Type, claim.Value)));
        claims.AddClaims(rights.Select(right => new Claim(right.Type, right.Value)));
        if (tenant is { } owner)
        {
            claims.AddClaim(new Claim(Tenant.ClaimType, owner.ToString()));
        }

        return new ClaimsPrincipal(claims);
    }

    private static HashSet<(string Type, string Value)> Read(string? recorded)
    {
        var rights = new HashSet<(string Type, string Value)>();
        if (string.IsNullOrEmpty(recorded))
        {
            return rights;
        }

        using var document = JsonDocument.Parse(recorded);
        foreach (var pair in document.RootElement.EnumerateArray())
        {
            rights.Add((pair[0].GetString() ?? string.Empty, pair[1].GetString() ?? string.Empty));
        }

        return rights;
    }
}
