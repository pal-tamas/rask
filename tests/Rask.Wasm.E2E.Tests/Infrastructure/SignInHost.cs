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

    public static async Task<SignInHost> StartAsync()
    {
        var wwwroot = Path.Combine(Repository.Root, "tests", "Rask.Wasm.Fixture.Wasm", "bin", Configuration,
            "net10.0-browser", "publish", "wwwroot");
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
        app.MapFallbackToFile("index.html", Files(files));

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
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
            context.Response.Redirect("/desk");
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
