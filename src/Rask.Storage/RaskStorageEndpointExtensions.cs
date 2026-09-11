using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Storage.Serving;

namespace Rask.Storage;

/// <summary>Maps the routes that serve stored files.</summary>
public static class RaskStorageEndpointExtensions
{
    internal const string RoutePrefix = "/_rask/files/";

    private static readonly string[] ReadMethods = [HttpMethods.Get, HttpMethods.Head];

    /// <summary>
    /// Maps <c>GET /_rask/files/public/{id}</c> (files saved as public) and <c>GET /_rask/files/{token}</c>
    /// (temporary URLs the app signs itself). Call it after <c>app.UseRask&lt;App&gt;()</c>, which sets the path
    /// base these routes live under.
    /// </summary>
    /// <remarks>
    /// Both routes allow anonymous requests: the public flag or the signed token is the authorization, and a
    /// fallback policy would otherwise break every <c>&lt;img&gt;</c> pointing at them. Chain your own
    /// conventions on the result — <c>app.MapRaskStorage().RequireRateLimiting("files")</c>.
    /// </remarks>
    public static IEndpointConventionBuilder MapRaskStorage(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var runtime = endpoints.ServiceProvider.GetService<StorageRuntime>()
                      ?? throw new InvalidOperationException(
                          "MapRaskStorage() needs the storage services. Register them first: "
                          + "builder.Services.AddRaskStorage<AppDbContext>();");
        runtime.EndpointsMapped = true;

        var group = endpoints.MapGroup(LiveOptions.PathBase + RoutePrefix.TrimEnd('/'));
        group.MapMethods("public/{id:guid}", ReadMethods, ServePublicAsync);
        group.MapMethods("{token:maxlength(" + TemporaryUrlProtector.MaxTokenChars + ")}", ReadMethods, ServeTemporaryAsync);
        group.AllowAnonymous();
        group.ExcludeFromDescription();
        return group;
    }

    private static Task ServePublicAsync(HttpContext context) =>
        context.Request.RouteValues["id"] is string raw && Guid.TryParse(raw, out var id)
            ? new StoredFileResult(id, StoredFileAccess.Public).ExecuteAsync(context)
            : StoredFileHeaders.NotFoundAsync(context);

    private static Task ServeTemporaryAsync(HttpContext context)
    {
        var protector = context.RequestServices.GetRequiredService<StorageRuntime>().Protector;
        return context.Request.RouteValues["token"] is string token && protector.TryUnprotect(token, out var id)
            ? new StoredFileResult(id, StoredFileAccess.Temporary).ExecuteAsync(context)
            : StoredFileHeaders.NotFoundAsync(context);
    }
}
