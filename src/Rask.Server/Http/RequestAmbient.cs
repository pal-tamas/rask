using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Http;

/// <summary>
///     Makes the HTTP request in progress the work whose services a static call reaches — <c>Current.Service&lt;T&gt;()</c>,
///     <c>Cache.Remember(…)</c> — when nothing more specific (an event, a render, a job) is ambient.
/// </summary>
/// <remarks>
///     Asked last, so it never shadows an event's own scope or a render's session; and it is the request's scope, never
///     the root provider. A live session's socket is not one: its work is the session's, and the session brackets each
///     piece of it with its own services. The accessor's flow is process-wide, so one reader serves every host in the process.
/// </remarks>
internal static class RequestAmbient
{
    private static readonly HttpContextAccessor Accessor = new();

    internal static void Register(IServiceCollection services)
    {
        // Registered, the host stamps each request into the accessor's flow; unregistered, it reads null.
        services.AddHttpContextAccessor();
        Ambient.RequestServices ??= static () => Accessor.HttpContext is { WebSockets.IsWebSocketRequest: false } request
            ? request.RequestServices
            : null;
    }
}
