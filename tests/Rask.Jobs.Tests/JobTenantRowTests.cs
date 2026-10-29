using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>A tenant's row as a job's handler reads it: <c>Current.Tenant</c>.</summary>
public sealed record TenantRowJob(string Tag) : IJob;

/// <summary>Records the tenant row the handler sees, as the generated <c>Current.Tenant</c> reads it.</summary>
public sealed class TenantRowJobHandler(Recorder recorder) : ICommandHandler<TenantRowJob>
{
    public Task Handle(TenantRowJob command)
    {
        recorder.Add($"{command.Tag}:{CurrentRows.Tenant<HeldShopRead>()?.Name ?? "none"}");
        return Task.CompletedTask;
    }
}

/// <summary>A tenant's read face, as a test stands one in: no database behind it.</summary>
public sealed class HeldShopRead : IReadModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";
}

/// <summary>
/// A job runs in the tenant its row recorded, and its handler reads that tenant's own row — read ahead by the
/// processor, so the handler's <c>Current.Tenant</c> never blocks on a query.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobTenantRowTests : IDisposable
{
    public void Dispose() => CurrentRows.Reset();

    [Fact]
    public async Task A_handler_reads_its_jobs_tenant_row_read_ahead_and_never_queries_for_it()
    {
        var acme = Guid.NewGuid();
        var shops = new Shops(new Dictionary<Guid, string> { [acme] = "Acme", [Guid.NewGuid()] = "Globex" });
        CurrentRows.Use(shops);
        await using var h = new JobsHarness();

        using (Tenant.Use(acme))
        {
            await h.Queue.Enqueue(new TenantRowJob("acme"), TestContext.Current.CancellationToken);
        }

        await h.Queue.Enqueue(new TenantRowJob("anon"), TestContext.Current.CancellationToken);
        await h.RunUntilAsync(() => h.Recorder.Values.Count == 2);

        Assert.Contains("acme:Acme", h.Recorder.Values);
        Assert.Contains("anon:none", h.Recorder.Values);
        Assert.Equal((1, 0), (shops.ReadAhead, shops.Blocking));
    }

    // The tenant rows as CurrentRows reads them, counting how: ahead of the work, or blocking inside it.
    private sealed class Shops(Dictionary<Guid, string> names) : CurrentRow(CurrentRowKind.Tenant, typeof(object))
    {
        private int _readAhead;
        private int _blocking;

        public int ReadAhead => Volatile.Read(ref _readAhead);

        public int Blocking => Volatile.Read(ref _blocking);

        protected override object? Load(Guid key)
        {
            Interlocked.Increment(ref _blocking);
            return Row(key);
        }

        protected override Task<object?> LoadAsync(Guid key, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readAhead);
            return Task.FromResult<object?>(Row(key));
        }

        protected override bool Owns(object row, Guid key) => ((HeldShopRead)row).Id == key;

        private HeldShopRead? Row(Guid key) =>
            names.TryGetValue(key, out var name) ? new HeldShopRead { Id = key, Name = name } : null;
    }
}
