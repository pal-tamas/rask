using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Rask.Server;

/// <summary>
///     The endpoint that says which build is running: <c>GET /version</c> answers
///     <c>{"version":"1.4.0+3f2a9c1"}</c> — the app's informational version, which MinVer stamps from the git tag.
/// </summary>
/// <remarks>
///     Mapped only when asked for, because it tells anyone who asks which build is live:
///     <c>app.MapEndpoints(e =&gt; e.MapRaskVersion())</c> on a <c>RaskApp</c>, or <c>app.MapRaskVersion()</c> on a host
///     wired by hand.
/// </remarks>
public static class RaskVersionEndpointExtensions
{
    /// <summary>
    ///     Maps <c>GET <paramref name="pattern" /></c> to the entry assembly's informational version as
    ///     <c>{"version":"…"}</c>. Anonymous — a deploy script or an uptime probe asks without signing in — served
    ///     <c>Cache-Control: no-store</c>, so a proxy never reports the build before last, and left out of the API
    ///     description.
    /// </summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <param name="pattern">The path it answers on (default <c>/version</c>).</param>
    /// <returns>The endpoint's convention builder, to require a policy or rate limit on it after all.</returns>
    public static IEndpointConventionBuilder MapRaskVersion(this IEndpointRouteBuilder endpoints, string pattern = "/version")
        => endpoints.MapRaskVersion(pattern, Assembly.GetEntryAssembly());

    internal static IEndpointConventionBuilder MapRaskVersion(
        this IEndpointRouteBuilder endpoints, string pattern, Assembly? app)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        // Written once: the version cannot change while the process runs.
        var body = VersionJson(VersionOf(app));

        return endpoints.MapGet(pattern, context =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength = body.Length;
                context.Response.Headers.CacheControl = "no-store";
                return context.Response.Body.WriteAsync(body, context.RequestAborted).AsTask();
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    // The informational version is MinVer's whole answer (a prerelease height, the commit); the assembly version
    // is only major.minor.patch, so it is the fallback for an app built without one.
    internal static string VersionOf(Assembly? app)
        => app?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? app?.GetName().Version?.ToString()
           ?? "0.0.0";

    private static byte[] VersionJson(string version)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("version", version);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
