using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Data;

namespace Rask.Auth.Tests;

/// <summary>An app wired the way the auth battery wires one, served by <c>TestServer</c>.</summary>
internal sealed class EndpointApp : IDisposable
{
    private readonly IHost _host;
    private readonly string _dbPath;

    /// <summary>The first-run token the app is configured with.</summary>
    public const string Token = "test-first-run-token";

    /// <param name="services">Registrations a test adds after the auth battery's.</param>
    /// <param name="endpoints">Endpoints a test maps beside <c>MapRaskAuth</c>.</param>
    public EndpointApp(Action<IServiceCollection>? services = null, Action<IEndpointRouteBuilder>? endpoints = null)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"rask-auth-endpoints-{Guid.NewGuid():N}.db");

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(registered =>
                {
                    registered.AddLogging(b => b.ClearProviders());
                    registered.AddRouting();
                    registered.AddRaskData();
                    registered.AddDbContextFactory<AuthDbContext>((sp, o) => o
                        .UseSqlite($"Data Source={_dbPath};Pooling=False")
                        .AddInterceptors(sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>()));
                    registered.AddSingleton(new PasswordHasher(iterations: 1_000));
                    registered.AddRaskAuth<AuthDbContext>(o =>
                    {
                        o.FirstRunToken = Token;
                        o.SignInAttemptsPerMinute = 3;
                    });
                    services?.Invoke(registered);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapRaskAuth();
                        endpoints?.Invoke(e);
                    });
                });
            })
            .Start();

        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public AuthDbContext NewContext() =>
        _host.Services.GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContext();

    /// <summary>A client that keeps cookies, over https.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>TestServer</c>'s own client does <b>not</b> keep cookies — its handler has no
    ///         container — so a session issued by one request would never reach the next one and every
    ///         follow-up would look signed-out. The handler below supplies one.
    ///     </para>
    ///     <para>
    ///         https, not http: the auth cookie is issued with <c>Secure</c>, and a cookie container
    ///         honours that — over http it would accept the <c>Set-Cookie</c> and then decline to send
    ///         it back, which is the same symptom for a different reason.
    ///     </para>
    /// </remarks>
    public HttpClient Client() =>
        new(new CookieHandler(_host.GetTestServer().CreateHandler()))
        {
            BaseAddress = new Uri("https://localhost"),
        };

    /// <summary>Carries cookies between requests, the way a browser does.</summary>
    private sealed class CookieHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly CookieContainer _cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = _cookies.GetCookieHeader(uri);

            if (!string.IsNullOrEmpty(header))
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var value in setCookies)
                {
                    _cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }
}
