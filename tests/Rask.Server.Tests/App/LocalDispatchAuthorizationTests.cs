using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Server.Tests.App;

/// <summary>
///     A handler's <c>[Authorize]</c> on a request dispatched in-process, through the host's own wiring: the user
///     of the request in flight is held to it, and a scope nobody owns — a job's, a hosted service's — is not.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class LocalDispatchAuthorizationTests
{
    [Fact]
    public async Task A_signed_in_user_without_the_role_is_refused_an_admin_only_command()
    {
        var app = await StartAsync();

        try
        {
            Assert.Equal("refused, signed in", await GetAsync(app, "/purge?roles=editor"));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task An_admin_is_admitted_to_an_admin_only_command()
    {
        var app = await StartAsync();

        try
        {
            Assert.Equal("purged", await GetAsync(app, "/purge?roles=admin"));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task An_anonymous_request_is_refused_as_not_signed_in()
    {
        var app = await StartAsync();

        try
        {
            Assert.Equal("refused, not signed in", await GetAsync(app, "/purge"));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_scope_nobody_owns_runs_the_command_as_the_system()
    {
        var app = await StartAsync();

        try
        {
            // What a job processor or a hosted service does: a scope of its own, with nobody signed in to it.
            await using var scope = app.Services.CreateAsyncScope();
            using var work = Ambient.Enter(scope.ServiceProvider);

            await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                .Send(new PurgeLogs(), TestContext.Current.CancellationToken);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task<WebApplication> StartAsync()
    {
        var app = RaskApp.Create(
            ["--applicationName", typeof(LocalDispatchAuthorizationTests).Assembly.GetName().Name!],
            builder => builder.WebHost.UseSetting("urls", "http://127.0.0.1:0"));

        app.MapEndpoints(endpoints => endpoints.MapGet("/purge", async (HttpContext context) =>
        {
            // Stands in for an authentication handler: the principal arrives on HttpContext.User.
            if (context.Request.Query["roles"].ToString() is { Length: > 0 } role)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));
            }

            try
            {
                await Dispatcher.Send(new PurgeLogs(), context.RequestAborted);
                return "purged";
            }
            catch (ForbiddenException refused)
            {
                return refused.IsAuthenticated ? "refused, signed in" : "refused, not signed in";
            }
        }));

        var built = app.Build<MinimalApp>();
        await built.StartAsync();
        return built;
    }

    private static async Task<string> GetAsync(WebApplication app, string path)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        return await client.GetStringAsync(path);
    }
}

[LocalOnly]
public sealed record PurgeLogs : ICommand;

[Authorize(Roles = "admin")]
public sealed class PurgeLogsHandler : ICommandHandler<PurgeLogs>
{
    public Task Handle(PurgeLogs command) => Task.CompletedTask;
}
