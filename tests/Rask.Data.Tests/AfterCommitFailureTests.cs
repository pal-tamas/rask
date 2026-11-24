using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>
///     <see cref="RaskDataOptions.AfterCommitFailures" />: a step that runs after a <c>Db.Transaction</c> commits and throws
///     no longer skips the steps behind it, and is logged — or, at <see cref="AfterCommitFailure.Throw" />, handed to the
///     caller once every step has run. Either way the work stays committed.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class AfterCommitFailureTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-after-commit-{Guid.NewGuid():N}.db");
    private ServiceProvider? _provider;

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_failing_step_after_the_commit_is_logged_and_the_steps_behind_it_still_run()
    {
        using var scope = Start(AfterCommitFailure.Log);
        var ct = TestContext.Current.CancellationToken;
        var behind = false;

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: ct);
            AmbientTransaction.Current!.AfterCommit(Failing("the notice failed"));
            AmbientTransaction.Current!.AfterCommit(() => behind = true);
        }, cancellationToken: ct);

        Assert.True(behind);
        Assert.Equal(1, await Widgets(ct));
    }

    [Fact]
    public async Task At_throw_the_caller_gets_the_failure_after_the_steps_behind_it_ran()
    {
        using var scope = Start(AfterCommitFailure.Throw);
        var ct = TestContext.Current.CancellationToken;
        var behind = false;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: ct);
            AmbientTransaction.Current!.AfterCommit(Failing("the notice failed"));
            AmbientTransaction.Current!.AfterCommit(() => behind = true);
        }, cancellationToken: ct));

        Assert.Equal("the notice failed", thrown.Message);
        Assert.True(behind);
        Assert.Equal(1, await Widgets(ct));
    }

    [Fact]
    public async Task At_throw_several_failing_steps_come_back_together()
    {
        using var scope = Start(AfterCommitFailure.Throw);
        var ct = TestContext.Current.CancellationToken;

        var thrown = await Assert.ThrowsAsync<AggregateException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: ct);
            AmbientTransaction.Current!.AfterCommit(Failing("the first step"));
            AmbientTransaction.Current!.AfterCommit(Failing("the second step"));
        }, cancellationToken: ct));

        Assert.Equal(["the first step", "the second step"], thrown.InnerExceptions.Select(e => e.Message));
    }

    private static Action Failing(string message) => () => throw new InvalidOperationException(message);

    private IDisposable Start(AfterCommitFailure failures)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskData(o => o.AfterCommitFailures = failures);
        services.AddRaskData<TestDbContext>();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));

        _provider = services.BuildServiceProvider();
        using (var db = _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        Db.Configure(_provider);
        var session = _provider.CreateScope();
        var bound = Db.UseScope(session.ServiceProvider);
        return new Both(bound, session);
    }

    private async Task<int> Widgets(CancellationToken ct)
    {
        await using var db = await _provider!.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync(ct);
        return await db.Set<Widget>().IgnoreQueryFilters().CountAsync(ct);
    }

    private sealed class Both(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }
}
