using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Hosting.Shared;
using Rask.Server.Authentication;
using Rask.Wire;

namespace Rask.Server;

/// <summary>Maps the tenant choice of an app that signs its users in itself.</summary>
public static class TenantChoiceExtensions
{
    /// <summary>
    /// Lets an administrator choose the tenant they act in, for an app whose sign-in is its own (a refresh-token cookie,
    /// a JWT): Rask answers the request and keeps its rules, the app writes the choice where its sign-in lives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rask owns the endpoint — <c>POST</c> to <see cref="TenantChoiceOptions.Path" /> with <c>{ "tenant": "…" }</c>,
    /// the <c>X-Rask-Auth</c> header, refused (<c>403</c>) when the browser says another site sent it — and who may
    /// choose: a signed-in user who holds <see cref="TenantChoiceOptions.Role" /> (admin) and does not
    /// <see cref="TenantChoiceOptions.BelongsToTenant" />. The same rules Rask.Auth's <c>auth.ActInTenant</c> keeps.
    /// </para>
    /// <para>
    /// The value is a <see cref="Guid" /> or a tenant's number; <paramref name="find" /> answers the tenant it names, or
    /// <see langword="null" /> for none (<c>400</c>). <paramref name="onChosen" /> is handed the request and the tenant —
    /// <see langword="null" /> when the administrator leaves — and writes it into the sign-in: a cookie tied to it, or a
    /// token issued again. It runs only for a choice that is allowed.
    /// </para>
    /// <para>
    /// A live page chooses from a click handler with <c>Auth.ActInTenant(id, returnUrl)</c> / <c>Auth.LeaveTenant()</c>:
    /// the choice reaches <paramref name="onChosen" /> on the browser's own request, and the page loads again at
    /// <c>returnUrl</c> in the chosen tenant. Map this before <c>MapRask</c>, whose fallback answers every path after it.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <param name="find">The tenant an id names, or <see langword="null" /> for one the app does not know.</param>
    /// <param name="onChosen">Writes the chosen tenant (<see langword="null" />: none) into the sign-in.</param>
    /// <param name="configure">The path, and who may choose.</param>
    /// <typeparam name="TTenant">What the app knows a tenant as.</typeparam>
    /// <returns>The endpoint, for further conventions.</returns>
    public static IEndpointConventionBuilder MapRaskTenantChoice<TTenant>(
        this IEndpointRouteBuilder endpoints,
        Func<Guid, HttpContext, Task<TTenant?>> find,
        Func<HttpContext, TTenant?, Task> onChosen,
        Action<TenantChoiceOptions>? configure = null)
        where TTenant : class
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(onChosen);

        var options = new TenantChoiceOptions();
        configure?.Invoke(options);
        var choice = new TenantChoice<TTenant>(find, onChosen, options);

        // The sign-in relay redeems a live handler's choice through the same rules; a host without AddRask has no relay.
        endpoints.ServiceProvider.GetService<TenantChoices>()?.Map(choice.Choose);

        return endpoints
            .MapPost(options.Path, (RequestDelegate)(context => AnswerAsync(context, choice)))
            // Antiforgery would be ASP.NET's token pair, which a front end would have to fetch and echo; the required
            // header is the same defence without the round trip, as on Rask.Auth's endpoints.
            .DisableAntiforgery();
    }

    /// <summary>
    /// <see cref="MapRaskTenantChoice{TTenant}(IEndpointRouteBuilder, Func{Guid, HttpContext, Task{TTenant}}, Func{HttpContext, TTenant, Task}, Action{TenantChoiceOptions})" />
    /// for an app whose tenants are numbered: <paramref name="find" /> is handed the number, and a
    /// <see cref="Guid" /> that carries none names no tenant.
    /// </summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <param name="find">The tenant a number names, or <see langword="null" /> for one the app does not know.</param>
    /// <param name="onChosen">Writes the chosen tenant (<see langword="null" />: none) into the sign-in.</param>
    /// <param name="configure">The path, and who may choose.</param>
    /// <typeparam name="TTenant">What the app knows a tenant as.</typeparam>
    /// <returns>The endpoint, for further conventions.</returns>
    public static IEndpointConventionBuilder MapRaskTenantChoice<TTenant>(
        this IEndpointRouteBuilder endpoints,
        Func<long, HttpContext, Task<TTenant?>> find,
        Func<HttpContext, TTenant?, Task> onChosen,
        Action<TenantChoiceOptions>? configure = null)
        where TTenant : class
    {
        ArgumentNullException.ThrowIfNull(find);

        return endpoints.MapRaskTenantChoice(
            (Guid id, HttpContext context) => TenantChoosing.TryNumber(id, out var number)
                ? find(number, context)
                : Task.FromResult<TTenant?>(null),
            onChosen,
            configure);
    }

    // The checks only a request from a page needs — the live relay's redeem made its own — then the choice itself.
    private static async Task AnswerAsync<TTenant>(HttpContext context, TenantChoice<TTenant> choice)
        where TTenant : class
    {
        // The answer changes a credential: no cache, shared or the browser's, keeps it.
        context.Response.Headers.CacheControl = "no-store";

        if (!context.Request.Headers.ContainsKey(AuthApi.RequestHeader))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!TenantChoosing.FromThisSite(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (await ReadTenantAsync(context).ConfigureAwait(false) is not { } request)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        context.Response.StatusCode = await choice.Choose(context, request.Tenant).ConfigureAwait(false);
    }

    // { "tenant": "…" } — a string, a number, or null to leave; null when the body is not that.
    private static async Task<ActInTenantRequest?> ReadTenantAsync(HttpContext context)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);
            if (body.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!body.RootElement.TryGetProperty("tenant", out var tenant) || tenant.ValueKind == JsonValueKind.Null)
            {
                return new ActInTenantRequest(null);
            }

            return tenant.ValueKind switch
            {
                JsonValueKind.String => new ActInTenantRequest(tenant.GetString()),
                JsonValueKind.Number => new ActInTenantRequest(tenant.GetRawText()),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
