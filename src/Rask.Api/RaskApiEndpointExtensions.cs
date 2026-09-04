using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Api;

/// <summary>
///     Maps a Rask app's HTTP endpoints.
/// </summary>
public static class RaskApiEndpointExtensions
{
    // Every verb the guard answers for. Rask's own catch-all is a MapGet, so without naming the verbs
    // here a POST to a wrong /api path would 405 rather than 404 — a status that says "this route
    // exists, not like that", which is exactly the wrong thing to tell someone with a typo.
    private static readonly string[] GuardedMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    /// <summary>
    ///     Maps this app's API: its controllers, and the guard that keeps a wrong URL under the API
    ///     prefix from being answered with the app.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>
    ///     The endpoint group, so an app can attach conventions to the whole API in one line —
    ///     <c>.RequireRateLimiting(...)</c>, CORS, output caching.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         <b>Where you call this does not decide whether your endpoints run.</b> Endpoint routing
    ///         matches on precedence, never on registration order, and every route an app writes is more
    ///         specific than Rask's <c>/{**path}</c> catch-all — so a controller answers from either side
    ///         of <c>UseRask</c>. Call it wherever the file reads best.
    ///     </para>
    ///     <para>
    ///         What order <em>cannot</em> fix, and this does: a request under the API prefix matching
    ///         <b>nothing</b>. Without the guard it reaches the catch-all and renders the app, so a
    ///         mistyped or deleted route answers <c>200</c> with a web page and the caller's JSON parse
    ///         fails somewhere far from the cause. The guard is an ordinary endpoint at the default order
    ///         rather than a fallback, deliberately: a fallback sits at <c>int.MaxValue</c> and would lose
    ///         to the catch-all, whereas at the same order <c>/api/{**path}</c> simply outranks
    ///         <c>/{**path}</c> — and loses in turn to every real route under it.
    ///     </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapRaskApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<ApiOptions>()
            ?? throw new InvalidOperationException(
                "MapRaskApi() found no API options. Call AddRaskApi() on the service collection first.");

        var group = endpoints.MapGroup(string.Empty);

        if (options.Controllers)
        {
            group.MapControllers();
        }

        if (options.NotFound)
        {
            var pattern = options.Prefix == "/"
                ? "/{**rest}"
                : options.Prefix + "/{**rest}";

            group.MapMethods(pattern, GuardedMethods, NotFoundAsync);
        }

        return group;
    }

    // RFC 9457, written by hand rather than through a serializer: it is four known fields, and the
    // package stays free of a JSON options dependency it would otherwise have to keep in step.
    private static async Task NotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";

        using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject();
        writer.WriteString("type", "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5");
        writer.WriteString("title", "Not Found");
        writer.WriteNumber("status", StatusCodes.Status404NotFound);
        writer.WriteString(
            "detail",
            $"No endpoint answers {context.Request.Method} {context.Request.Path}.");
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }
}
