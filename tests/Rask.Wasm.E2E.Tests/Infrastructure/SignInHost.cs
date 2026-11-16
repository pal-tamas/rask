using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Wasm.E2E.Tests.Infrastructure;

/// <summary>
///     The fixture app's own server, in this process on a loopback port: it serves the published bundle, issues and
///     takes the cookie, answers <c>/api/auth/me</c> as a Rask.Auth server does, and has one API that refuses nobody
///     with a 401. Nothing in it is Rask's; a WASM app's server may be anything.
/// </summary>
/// <remarks>
///     Given a mount path it serves the app under that sub-path, as a host beside another site does: the bundle, the
///     API and the pages answer only under the mount, everything outside it is a 404, and the shell's
///     <c>&lt;base href="/"/&gt;</c> is served as <c>&lt;base href="{mount}/"/&gt;</c> — the one edit a publish with
///     <c>RaskPathBase</c> makes (<c>_RaskRewriteBaseHref</c> in Rask.Wasm.targets), made here so that one publish
///     serves both the root journeys and the sub-path ones.
/// </remarks>
internal sealed class SignInHost : IAsyncDisposable
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private readonly WebApplication _app;

    private SignInHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    /// <summary>Every API request the host answered, as <c>path:status</c>, in order.</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>Every path the browser asked for, mount included, in the order the requests arrived.</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    /// <param name="mountPath">The sub-path the app is served under, such as <c>/app</c>; empty serves it at the root.</param>
    public static async Task<SignInHost> StartAsync(string mountPath = "")
    {
        var wwwroot = Path.Combine(Repository.Root, "tests", "Rask.Wasm.Fixture.Wasm", "bin", Configuration,
            "net11.0-browser", "publish", "wwwroot");
        if (!File.Exists(Path.Combine(wwwroot, "index.html")))
        {
            throw new InvalidOperationException(
                $"The WASM fixture is not published at '{wwwroot}'. Run scripts/run-wasm-e2e-local.sh, which publishes it.");
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.Cookie.Name = "fixture-auth");
        var app = builder.Build();
        var files = new PhysicalFileProvider(wwwroot);
        SignInHost? host = null;
        var mount = mountPath.TrimEnd('/');
        var shell = (await File.ReadAllTextAsync(Path.Combine(wwwroot, "index.html")))
            .Replace("<base href=\"/\"/>", $"<base href=\"{mount}/\"/>", StringComparison.Ordinal);

        app.Use((context, next) =>
        {
            host?.Requests.Enqueue(context.Request.Path);
            return next(context);
        });
        if (mount.Length > 0)
        {
            app.UsePathBase(mount);
            app.Use((context, next) =>
            {
                if (context.Request.PathBase.HasValue)
                {
                    return next(context);
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        }

        // After the path base, so the endpoints match the path under the mount.
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            await next(context);
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth"))
            {
                host?.Calls.Enqueue($"{context.Request.Path}:{context.Response.StatusCode}");
            }
        });
        app.UseAuthentication();
        MapAccount(app);
        app.UseStaticFiles(Files(files));
        app.MapFallback(context => Shell(context, shell));

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        host = new SignInHost(app, address);
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static void MapAccount(WebApplication app)
    {
        app.MapGet("/token", async context =>
        {
            var user = context.Request.Query["user"].ToString();
            await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)],
                CookieAuthenticationDefaults.AuthenticationScheme)));
            context.Response.Redirect(context.Request.PathBase + "/desk");
        });

        // As Rask.Auth answers it: 204 for nobody.
        app.MapGet("/api/auth/me", context => context.User.Identity?.IsAuthenticated == true
            ? context.Response.WriteAsJsonAsync(new { id = context.User.Identity.Name, email = context.User.Identity.Name, roles = Array.Empty<string>() })
            : NoContent(context));

        app.MapPost("/auth/logout", async context =>
        {
            await context.SignOutAsync();
            await NoContent(context);
        });

        app.MapGet("/api/note", context =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            return context.Response.WriteAsync($"note for {context.User.Identity.Name}");
        });
    }

    // Every route the app owns answers with its shell, as the SDK's index.html fallback does.
    private static Task Shell(HttpContext context, string shell)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        return context.Response.WriteAsync(shell);
    }

    private static Task NoContent(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    // The runtime's own files (.dat, .blat) are not in the default table; served as bytes, as a static host serves them.
    private static StaticFileOptions Files(IFileProvider files) => new()
    {
        FileProvider = files,
        ContentTypeProvider = new FileExtensionContentTypeProvider(),
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream",
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
    };
}
