using Microsoft.AspNetCore.Http;
using Rask.Hosting.Shared;

namespace Rask.Server.Authentication;

/// <summary>
/// One tenant choice for an app with its own sign-in: who may choose (the rules Rask.Auth's choice keeps), the tenant
/// the app knows by that id, and the app's own hand writing it into its sign-in.
/// </summary>
internal sealed class TenantChoice<TTenant>(
    Func<Guid, HttpContext, Task<TTenant?>> find,
    Func<HttpContext, TTenant?, Task> onChosen,
    TenantChoiceOptions options)
    where TTenant : class
{
    /// <summary>
    /// Chooses the tenant <paramref name="value" /> names — a <see cref="Guid" />, a tenant's number, or nothing to leave
    /// — for the request's user. <c>204</c>; <c>401</c> for nobody; <c>403</c> for a user who may not choose;
    /// <c>400</c> for a value that names no tenant, or one <c>find</c> does not know. Nothing reaches
    /// <c>onChosen</c> unless it answers <c>204</c>.
    /// </summary>
    public async Task<int> Choose(HttpContext context, string? value)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (!TenantChoosing.MayChoose(options.BelongsToTenant(user), options.Role, user.IsInRole))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (!TenantChoosing.TryRead(value, out var tenant))
        {
            return StatusCodes.Status400BadRequest;
        }

        TTenant? chosen = null;
        if (tenant is { } id && (chosen = await find(id, context).ConfigureAwait(false)) is null)
        {
            return StatusCodes.Status400BadRequest;
        }

        await onChosen(context, chosen).ConfigureAwait(false);
        return StatusCodes.Status204NoContent;
    }
}
