using System.Collections.Concurrent;

namespace Rask.Testing.Fixture.App;

/// <summary>
///     A sign-in the app holds on its side, as Rask.Auth does: a <c>session</c> cookie naming one it handed out, which a
///     sign-out ends, so a copy of the cookie kept elsewhere no longer signs anyone in. Asked the way Rask.Auth is asked:
///     <c>GET /api/auth/me</c> answers <c>200</c> for a sign-in that holds and <c>204</c> for nobody.
/// </summary>
public static class Sessions
{
    private const string Cookie = "session";

    private static readonly ConcurrentDictionary<string, string> Open = new(StringComparer.Ordinal);

    /// <summary>Maps the sign-in (<c>GET /fixture/sign-in/{name}</c>), <c>/api/auth/me</c> and <c>/api/auth/logout</c>.</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/fixture/sign-in/{name}", (string name, HttpContext http) =>
        {
            var session = Guid.NewGuid().ToString("N");
            Open[session] = name;
            http.Response.Cookies.Append(Cookie, session);
            http.Response.Cookies.Append("who", name);
            return Results.Text($"Signed in as {name}");
        });
        endpoints.MapGet("/api/auth/me", (HttpContext http) =>
            http.Request.Cookies.TryGetValue(Cookie, out var session) && session is not null
            && Open.TryGetValue(session, out var name)
                ? Results.Json(new { name })
                : Results.NoContent());
        endpoints.MapPost("/api/auth/logout", (HttpContext http) =>
        {
            if (http.Request.Cookies.TryGetValue(Cookie, out var session) && session is not null)
            {
                Open.TryRemove(session, out _);
            }

            http.Response.Cookies.Delete(Cookie);
            http.Response.Cookies.Delete("who");
            return Results.NoContent();
        });
    }
}
