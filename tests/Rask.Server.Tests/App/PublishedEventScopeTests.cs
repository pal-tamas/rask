using System.Net.WebSockets;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Data;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.App;

/// <summary>
///     An event published from inside a scope of the publisher's own — a job running in the tenant its row recorded, as
///     the user who enqueued it — reaches an open page in that PAGE's tenant and as its user. Inherited, a Hunland user
///     was shown Klapcsik's pumps while a Klapcsik pump was being asked.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class PublishedEventScopeTests : IDisposable
{
    private static readonly Guid Hunland = Guid.NewGuid();
    private static readonly Guid Klapcsik = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid TheJobsUser = Guid.NewGuid();

    public void Dispose() => Db.Reset();

    [Fact]
    public async Task A_page_hears_an_event_a_job_published_in_another_tenant_in_its_own_tenant_and_as_its_own_user()
    {
        using var host = CreateHost();
        using var ws = await OpenAsBobOfHunlandAsync(host);
        var log = host.Server.Services.GetRequiredService<ScopeLog>();

        await Task.Run(async () =>
        {
            using var tenant = Tenant.Use(Klapcsik);
            using var user = Current.UseUser(TheJobsUser);
            await host.Server.Services.GetRequiredService<IDispatcher>().Publish(new PumpsChanged());
        }, TestContext.Current.CancellationToken);
        await WaitFor.True(() => log.Heard.Count == 1, "the page hears it");

        Assert.Equal((Hunland, Bob), log.Heard.Single());
    }

    [Fact]
    public async Task The_events_own_handler_still_runs_in_the_publishers_tenant_and_as_its_user()
    {
        using var host = CreateHost();
        using var ws = await OpenAsBobOfHunlandAsync(host);
        var log = host.Server.Services.GetRequiredService<ScopeLog>();

        await Task.Run(async () =>
        {
            using var tenant = Tenant.Use(Klapcsik);
            using var user = Current.UseUser(TheJobsUser);
            await host.Server.Services.GetRequiredService<IDispatcher>().Publish(new PumpsChanged());
        }, TestContext.Current.CancellationToken);
        await WaitFor.True(() => log.Heard.Count == 1, "the page hears it");

        Assert.Equal((Klapcsik, TheJobsUser), log.Handled.Single());
    }

    [Fact]
    public async Task A_subscriber_reads_an_event_a_job_published_in_another_tenant_in_its_own_tenant_and_as_its_own_user()
    {
        using var host = CreateHost();
        var dispatcher = host.Server.Services.GetRequiredService<IDispatcher>();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heard = SubscribeAsBobOfHunland(dispatcher, connected);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await Task.Run(async () =>
        {
            using var tenant = Tenant.Use(Klapcsik);
            using var user = Current.UseUser(TheJobsUser);
            await dispatcher.Publish(new PumpsChanged());
        }, TestContext.Current.CancellationToken);

        Assert.Equal((Hunland, Bob), await heard.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    // A subscription opened in Hunland as Bob: the scope of the page that opened it.
    private static async Task<(Guid?, Guid?)> SubscribeAsBobOfHunland(IDispatcher dispatcher, TaskCompletionSource connected)
    {
        using var tenant = Tenant.Use(Hunland);
        using var user = Current.UseUser(Bob);
        await using var events = dispatcher.Subscribe<PumpsChanged>(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = events.MoveNextAsync();
        connected.TrySetResult();
        Assert.True(await next);
        return (Tenant.Key, Current.UserId);
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<PumpsPage>(services =>
        {
            DataHostSeams.Add(services);
            services.AddRaskCqrs();
            services.AddSingleton<ScopeLog>();
        });

    private static async Task<WebSocket> OpenAsBobOfHunlandAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var session = host.Store.Peek(sessionId)!;
        var settled = session.AttachesSettled;
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user");
        await session.LastHandlerTask;

        // Once the attach has adopted the connection's own (anonymous) principal: set before, it would be replaced.
        session.Users.Set(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Bob.ToString()), new Claim(Tenant.ClaimType, Hunland.ToString())],
            "Test")));
        await ws.SettledAsync();
        return ws;
    }
}

public sealed record PumpsChanged : IEvent;

/// <summary>Which tenant and which user each side of a published event ran in.</summary>
public sealed class ScopeLog
{
    private readonly Lock _gate = new();
    private readonly List<(Guid?, Guid?)> _heard = [];
    private readonly List<(Guid?, Guid?)> _handled = [];

    public IReadOnlyList<(Guid?, Guid?)> Heard
    {
        get
        {
            lock (_gate)
            {
                return [.. _heard];
            }
        }
    }

    public IReadOnlyList<(Guid?, Guid?)> Handled
    {
        get
        {
            lock (_gate)
            {
                return [.. _handled];
            }
        }
    }

    public void Hear() => Add(_heard);

    public void Handle() => Add(_handled);

    private void Add(List<(Guid?, Guid?)> into)
    {
        lock (_gate)
        {
            into.Add((Tenant.Key, Current.UserId));
        }
    }
}

// The event's own in-process handler: part of the publisher's work.
public sealed class PumpsChangedHandler(ScopeLog log) : IEventHandler<PumpsChanged>
{
    public Task Handle(PumpsChanged e)
    {
        log.Handle();
        return Task.CompletedTask;
    }
}

public sealed partial class PumpsPage(ScopeLog log) : Component, IEventHandler<PumpsChanged>
{
    public Task Handle(PumpsChanged e)
    {
        log.Hear();
        return Task.CompletedTask;
    }

    protected override Component? Render() => Markup.P["pumps"];
}
