using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server;

/// <summary>
///     The endpoint that says which release is running: <c>GET /version</c> answers <c>{"version":"…"}</c> — the
///     configured <c>Rask:Version</c> (<c>rask deploy</c> sets <c>Rask__Version</c> to the UTC moment of the deploy,
///     <c>v26.11.11.13.12.02</c>), else a <c>version.json</c> beside the app (<c>{"version":"…"}</c>, the shape the
///     endpoint answers), else the app's informational version, which MinVer stamps from the git tag.
/// </summary>
/// <remarks>
///     Mapped only when asked for, because it tells anyone who asks which build is live:
///     <c>app.MapEndpoints(e =&gt; e.MapRaskVersion())</c> on a <c>RaskApp</c>, or <c>app.MapRaskVersion()</c> on a host
///     wired by hand. Map it outside any <c>/api</c> group, so a filter the app puts on its API never hides it from a
///     deploy's health check.
/// </remarks>
public static class RaskVersionEndpointExtensions
{
    /// <summary>The configuration key a release name is read from first — <c>Rask__Version</c> in the environment.</summary>
    internal const string ConfigurationKey = "Rask:Version";

    /// <summary>The file beside the app a release name is read from when configuration names none.</summary>
    internal const string FileName = "version.json";

    /// <summary>
    ///     Maps <c>GET <paramref name="pattern" /></c> to the running release as <c>{"version":"…"}</c>: the configured
    ///     <c>Rask:Version</c>, else <c>version.json</c> beside the app, else the entry assembly's informational version.
    ///     Anonymous — a deploy script or an uptime probe asks without signing in — served
    ///     <c>Cache-Control: no-store</c>, so a proxy never reports the build before last, and left out of the API
    ///     description.
    /// </summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <param name="pattern">The path it answers on (default <c>/version</c>).</param>
    /// <returns>The endpoint's convention builder, to require a policy or rate limit on it after all.</returns>
    public static IEndpointConventionBuilder MapRaskVersion(this IEndpointRouteBuilder endpoints, string pattern = "/version")
        => endpoints.MapRaskVersion(pattern, Assembly.GetEntryAssembly(), AppContext.BaseDirectory);

    internal static IEndpointConventionBuilder MapRaskVersion(
        this IEndpointRouteBuilder endpoints, string pattern, Assembly? app, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        // Written once: the release cannot change while the process runs (a deploy starts a new container).
        var configured = endpoints.ServiceProvider.GetService<IConfiguration>()?[ConfigurationKey];
        var body = VersionJson(VersionOf(configured, directory, app));

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

    /// <summary>
    ///     The release name, first that names one: <paramref name="configured" />, <see cref="FileName" /> in
    ///     <paramref name="directory" />, the assembly's own version.
    /// </summary>
    internal static string VersionOf(string? configured, string? directory, Assembly? app)
        => Named(configured) ?? FromFile(directory) ?? VersionOf(app);

    // The informational version is MinVer's whole answer (a prerelease height, the commit); the assembly version
    // is only major.minor.patch, so it is the fallback for an app built without one.
    internal static string VersionOf(Assembly? app)
        => app?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? app?.GetName().Version?.ToString()
           ?? "0.0.0";

    // `{"version":"…"}` beside the app: what a pipeline writes into the publish folder when it names releases itself.
    // A file of any other shape names nothing, and the assembly's version answers instead.
    private static string? FromFile(string? directory)
    {
        var path = directory is null ? null : Path.Combine(directory, FileName);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty("version", out var version)
                   && version.ValueKind == JsonValueKind.String
                ? Named(version.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Named(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
