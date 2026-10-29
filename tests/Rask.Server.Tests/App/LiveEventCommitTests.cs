using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>A bollard that says it was raised: the domain event its save publishes once it has committed.</summary>
public sealed class Bollard : Aggregate<Guid>, IFormModel
{
    private Bollard() { }

    public string Name { get; private set; } = "";

    public static Bollard Raised(string name)
    {
        var bollard = new Bollard { Id = Guid.NewGuid(), Name = name };
        bollard.Raise(new BollardRaised(name));
        return bollard;
    }
}

public sealed record BollardRaised(string Name) : IEvent;

public sealed record QuayNotice(string Name) : IEvent;

/// <summary>What the quay page heard, and when.</summary>
public sealed class QuayLog
{
    private readonly Lock _gate = new();

    public List<string> Heard { get; } = [];

    public void Hear(string what)
    {
        lock (_gate)
        {
            Heard.Add(what);
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return Heard.Count;
            }
        }
    }
}

/// <summary>A page that handles both events: a bollard an aggregate raised, and a notice published by hand.</summary>
public sealed partial class QuayApp(QuayLog log) : Component, IEventHandler<BollardRaised>, IEventHandler<QuayNotice>
{
    public Task Handle(BollardRaised e)
    {
        log.Hear($"raised {e.Name}");
        return Task.CompletedTask;
    }

    public Task Handle(QuayNotice e)
    {
        log.Hear($"notice {e.Name}");
        return Task.CompletedTask;
    }

    protected override Component? Render() => Markup.P["quay"];
}

/// <summary>
///     What a live page's listener hears of the data layer's events: a domain event once its save has committed, and
///     an event published inside <c>Db.Transaction</c> once the transaction has — never after a rollback.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class LiveEventCommitTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-live-events-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Db.Reset();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_domain_event_an_aggregate_raised_reaches_the_open_page_s_listener()
    {
        var app = await StartAsync();
        var log = app.Services.GetRequiredService<QuayLog>();

        try
        {
            await GetAsync(app, "/");
            await GetAsync(app, "/bollards/raise?name=North");
            await WaitFor.True(() => log.Count == 1, "the page hears the bollard");

            Assert.Equal(["raised North"], log.Heard);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task An_event_published_inside_a_transaction_is_heard_only_once_it_commits()
    {
        var app = await StartAsync();
        var log = app.Services.GetRequiredService<QuayLog>();

        try
        {
            await GetAsync(app, "/");
            var heardBeforeTheCommit = await GetAsync(app, "/notices?name=Kept");
            await WaitFor.True(() => log.Count == 1, "the page hears the notice");

            Assert.Equal("0", heardBeforeTheCommit);
            Assert.Equal(["notice Kept"], log.Heard);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task An_event_published_inside_a_transaction_that_rolls_back_is_never_heard()
    {
        var app = await StartAsync();
        var log = app.Services.GetRequiredService<QuayLog>();

        try
        {
            await GetAsync(app, "/");
            await GetAsync(app, "/notices?name=Dropped&fail=true");
            await GetAsync(app, "/notices?name=After");
            await WaitFor.True(() => log.Count == 1, "the page hears the notice after it");

            Assert.Equal(["notice After"], log.Heard);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task<WebApplication> StartAsync()
    {
        var app = RaskApp.Create([], builder =>
        {
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(
                [new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", $"Data Source={_dbPath};Pooling=False")]);
            builder.Services.AddSingleton<QuayLog>();
        });

        app.MapEndpoints(endpoints =>
        {
            endpoints.MapGet("/bollards/raise", async (string name) =>
            {
                await Bollard.Raised(name).Save();
                return "raised";
            });

            // Publishes, then waits inside the transaction long enough for a delivery that did not wait to land.
            endpoints.MapGet("/notices", async (string name, bool? fail, QuayLog log) =>
            {
                var heard = 0;
                try
                {
                    await Db.Transaction(async () =>
                    {
                        await Mooring.Named(name).Save();
                        await Dispatcher.Publish(new QuayNotice(name));
                        await Task.Delay(300);
                        heard = log.Heard.Count(static h => h.StartsWith("notice", StringComparison.Ordinal));
                        if (fail == true)
                        {
                            throw new InvalidOperationException("rolled back");
                        }
                    });
                }
                catch (InvalidOperationException)
                {
                    return "rolled back";
                }

                return heard.ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
        });

        var built = app.Build<QuayApp>();
        await using (var db = await built.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>()
                         .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await built.StartAsync(TestContext.Current.CancellationToken);
        return built;
    }

    private static async Task<string> GetAsync(WebApplication app, string path)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        return await client.GetStringAsync(path, TestContext.Current.CancellationToken);
    }
}
