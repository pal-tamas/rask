using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.Background.Tests;

public sealed record ParcelSent(Guid Parcel) : IEvent;

public sealed record ParcelReturned(Guid Parcel) : IEvent;

public sealed record PrintLabel(Guid Parcel) : IJob;

/// <summary>An aggregate whose sending raises the event a save handler enqueues a job for.</summary>
public sealed class Parcel : Aggregate<Guid>
{
    private Parcel() { }

    public static Parcel Send()
    {
        var parcel = new Parcel { Id = Guid.NewGuid() };
        parcel.Raise(new ParcelSent(parcel.Id));
        return parcel;
    }

    public static Parcel Return()
    {
        var parcel = new Parcel { Id = Guid.NewGuid() };
        parcel.Raise(new ParcelReturned(parcel.Id));
        return parcel;
    }
}

public sealed class EnqueueLabel : IEventHandler<ParcelSent>
{
    public async Task Handle(ParcelSent e) => await Jobs.Enqueue(new PrintLabel(e.Parcel));
}

// Takes the queue in its constructor, as a class of the app's may: it is handed the database's fake too.
public sealed class EnqueueReturnLabel(IJobs jobs) : IEventHandler<ParcelReturned>
{
    public Task Handle(ParcelReturned e) => jobs.Add(new PrintLabel(e.Parcel), at: null, after: null, lane: null);
}

public sealed class PrintLabelHandler(Recorder recorder) : IJobHandler<PrintLabel>
{
    public Task Handle(PrintLabel job)
    {
        recorder.Add($"label:{job.Parcel}");
        return Task.CompletedTask;
    }
}

/// <summary>An app's context, mapping the job table as a RaskApp's does.</summary>
public sealed class ParcelContext(DbContextOptions<ParcelContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskJobs();
    }
}

/// <summary>A <c>TestDatabase</c> over a model that maps the job table stands the job queue in with its fake.</summary>
public sealed class JobsTestDatabaseTests : IDisposable
{
    private readonly InMemorySqlite _db = new();
    private readonly Recorder _recorder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_save_handler_s_enqueue_is_recorded_by_the_database_s_fake_and_runs_with_no_host()
    {
        await using var database = await Start();
        var parcel = Parcel.Send();

        database.Context.Add(parcel);
        await database.Context.SaveChangesAsync(Ct);
        await database.Jobs.Run(Ct);

        database.Jobs.Enqueued<PrintLabel>().Once();
        Assert.Equal([$"label:{parcel.Id}"], _recorder.Values);
    }

    [Fact]
    public async Task A_handler_that_injects_the_queue_is_handed_the_database_s_fake()
    {
        await using var database = await Start();

        database.Context.Add(Parcel.Return());
        await database.Context.SaveChangesAsync(Ct);

        database.Jobs.Enqueued<PrintLabel>().Once();
    }

    [Fact]
    public async Task The_test_s_own_line_enqueues_into_the_same_fake()
    {
        await using var database = await Start();

        await Jobs.Enqueue(new PrintLabel(Guid.Empty), Ct);

        database.Jobs.Enqueued<PrintLabel>().Once();
    }

    [Fact]
    public async Task A_fake_the_test_made_first_is_the_one_the_database_records_in()
    {
        using var jobs = Jobs.Fake();
        await using var database = await Start();

        database.Context.Add(Parcel.Send());
        await database.Context.SaveChangesAsync(Ct);

        Assert.Same(jobs, database.Jobs);
        jobs.Enqueued<PrintLabel>().Once();
    }

    [Fact]
    public async Task Disposing_the_database_puts_the_battery_back()
    {
        var database = await Start();

        await database.DisposeAsync();

        Assert.False(Jobs.IsOn);
    }

    private Task<TestDatabase<ParcelContext>> Start() =>
        TestDatabase.Start<ParcelContext>(
            o => o.UseSqlite(_db.ConnectionString), services: s => s.AddSingleton(_recorder), cancellationToken: Ct);
}
