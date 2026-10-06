using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

// Outermost, ahead of validation: a caller who may not send a request should not learn what is wrong with it.
// The endpoint has always checked a handler's [Authorize] for a request that arrives over HTTP; this is the same
// check for one dispatched in-process, where a page calling Dispatcher.Send would otherwise walk straight past it.
/// <summary>
///     Refuses a dispatched request with a <see cref="ForbiddenException" /> when its handler's
///     <c>[Authorize]</c> does not admit the user the work in flight is for.
/// </summary>
internal sealed class AuthorizationBehavior<TRequest, TResult>(IServiceProvider services) : IPipelineBehavior<TRequest, TResult>
{
    public Task<TResult> Handle(TRequest request, RequestHandler<TResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        // Work that is nobody's — a job, a durable handler, a hosted service — has no principal and runs as the system.
        return CqrsRegistry.FindAuthorization(typeof(TRequest)) is { } declared
               && services.GetService<IDispatchPrincipal>()?.Current is { } user
            ? Checked(user, declared, next)
            : next();
    }

    private async Task<TResult> Checked(ClaimsPrincipal user, RequestAuthorization declared, RequestHandler<TResult> next)
    {
        var refusal = await RequestAccess
            .Refusal(user, typeof(TRequest).Name, requiresAuthentication: true, declared.RoleSets, declared.Policies, services)
            .ConfigureAwait(false);

        return refusal is null ? await next().ConfigureAwait(false) : throw refusal;
    }
}
