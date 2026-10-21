using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>A mooring at a quay: one row, nothing scoped, so the transaction is all a test has to watch.</summary>
public sealed class Mooring : Aggregate<Guid, Model>
{
    private Mooring() { }

    public string Name { get; private set; } = "";

    public static Mooring Named(string name) => new() { Id = Guid.NewGuid(), Name = name };
}

/// <summary>Two moorings, written by a job in one transaction.</summary>
public sealed record PairMoorings(string Name) : IJob;

public sealed class PairMooringsHandler : ICommandHandler<PairMoorings>
{
    public Task Handle(PairMoorings command) => DbTransactionRaskAppTests.Pair(command.Name, fail: false);
}

/// <summary>
///     <c>Db.Transaction</c> on a <c>RaskApp</c> host, with nothing wired by the app: from an HTTP endpoint and from a
///     job, every write inside commits together, and a throw takes them all back.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class DbTransactionRaskAppTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-transaction-app-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Db.Reset();
        File.Delete(_dbPath);
    }

    internal static Task Pair(string name, bool fail) =>
        Db.Transaction(async () =>
        {
            await Mooring.Named(name).Save();
            await Mooring.Named($"{name}-2").Save();
            if (fail)
            {
                throw new InvalidOperationException("rolled back");
            }
        });

    [Fact]
    public async Task An_endpoint_s_writes_commit_together_and_a_throw_takes_them_all_back()
    {
        var app = await StartAsync();

        try
        {
            var saved = await GetAsync(app, "/moorings/pair?name=A");
            var failed = await GetAsync(app, "/moorings/pair?name=B&fail=true");
            var moorings = await GetAsync(app, "/moorings");

            Assert.Equal(("saved", "rolled back"), (saved, failed));
            Assert.Equal("A,A-2", moorings);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_job_s_writes_commit_together()
    {
        var app = await StartAsync();

        try
        {
            await GetAsync(app, "/moorings/enqueue?name=J");
            var moorings = "";
            for (var deadline = DateTime.UtcNow + WaitFor.HangCeiling; moorings.Length == 0 && DateTime.UtcNow < deadline;)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
                moorings = await GetAsync(app, "/moorings");
            }

            // Never one without the other: the job's two saves are one commit.
            Assert.Equal("J,J-2", moorings);
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
        });

        app.MapEndpoints(endpoints =>
        {
            endpoints.MapGet("/moorings/pair", async (string name, bool? fail) =>
            {
                try
                {
                    await Pair(name, fail ?? false);
                    return "saved";
                }
                catch (InvalidOperationException e)
                {
                    return e.Message;
                }
            });
            endpoints.MapGet("/moorings/enqueue", async (string name) =>
            {
                await Jobs.Enqueue(new PairMoorings(name));
                return "enqueued";
            });
            endpoints.MapGet("/moorings", async () =>
                string.Join(',', await Mooring.OrderBy(b => b.Name).Select(b => b.Name)));
        });

        var built = app.Build<MinimalApp>();
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
