using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Cqrs.Server;
using Rask.Data;

namespace Rask;

/// <summary>
///     What the host has to supply for the data layer to know who and which tenant the work in flight is for —
///     the same for a <c>RaskApp</c> and for a host wired by hand with <c>AddRaskData&lt;TContext&gt;(…)</c>.
/// </summary>
/// <remarks>
///     One place on purpose: the two hosts used to be free to drift, and the one an app wires itself was the one
///     that silently went without a session scope — a live page's reads then had no tenant at all.
/// </remarks>
internal static class DataHostSeams
{
    /// <summary>Registers the session scope, the principal source and what a save tells the session.</summary>
    internal static void Add(IServiceCollection services)
    {
        // Once: a host that calls AddRaskData<TContext>(…) for two contexts wires one set of seams.
        if (services.Any(static d => d.ImplementationType == typeof(SessionDataScope)))
        {
            return;
        }

        services.AddRaskData();

        // A live session's reads have to see the tenant of the user that session belongs to, and a read
        // is a static call that runs outside any DI scope. This makes the session's own scope ambient
        // for the duration of its work; Rask.Server brackets with it, Rask.Data reads through it.
        services.AddSingleton<ISessionWorkScope, SessionDataScope>();

        // Scoped, because the principal it reads is: this instance belongs to one session or request, and
        // SessionDataScope above (and the request middleware below) is what makes it reachable from a
        // read, a write's tenant stamp and Current.UserId.
        services.AddScoped<ClaimsPrincipalSource>();
        services.AddScoped<IPrincipalSource>(static sp => sp.GetRequiredService<ClaimsPrincipalSource>());

        // The same user is who a handler's [Authorize] is held to when a page dispatches to it in-process, and
        // ASP.NET's authorization is what decides a policy it names.
        services.AddSingleton<IDispatchPrincipal, DispatchPrincipal>();
        services.TryAddTransient<IPolicyEvaluator, AuthorizationPolicyEvaluator>();

        // A write refreshes the queries about what it wrote, on the screen of the session that made it:
        // Person.Create(model) refetches QueryKey.For<Person> queries with no invalidation to write.
        // Rask.Query is always here with data — both need the mediator — and the scope is the same one
        // SessionDataScope makes ambient.
        services.AddScoped<IDataChanges, QueryDataChanges>();
    }

    /// <summary>
    ///     Makes each request's services ambient for the data layer, as <see cref="SessionDataScope" /> does for
    ///     a live session's work.
    /// </summary>
    /// <remarks>
    ///     An API endpoint, a CQRS endpoint or a controller then reads <c>Current.UserId</c>, filters by the
    ///     tenant in flight and stamps an insert with it — none of which can take a parameter, because a read or
    ///     a factory is a static call. Placed after authentication, so the principal it hands over is real.
    /// </remarks>
    internal static void UseRequestScope(IApplicationBuilder app) =>
        app.Use(static async (context, next) =>
        {
            if (context.RequestServices.GetService<ClaimsPrincipalSource>() is { } principal)
            {
                principal.Request = context;
            }

            // OpenScope, so an app's tenant resolver (AddRaskTenant) is asked here, as the request starts.
            using (Db.OpenScope(context.RequestServices))
            using (Ambient.Enter(context.RequestAborted))
            {
                await next(context).ConfigureAwait(false);
            }
        });
}
