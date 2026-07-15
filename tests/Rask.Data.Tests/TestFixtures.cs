using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// A test aggregate exercising all three opt-ins: audit stamps (via the base), soft delete, and a
// concurrency version — and it raises a domain event when renamed.
public sealed class Widget : AggregateRoot<Guid>, ISoftDeletable, IVersioned
{
    private Widget() { } // EF materialization

    public string Name { get; private set; } = "";

    public DateTime? DeletedAt { get; private set; }

    public int Version { get; private set; }

    public static Widget Create(string name)
    {
        var widget = new Widget { Id = Guid.NewGuid(), Name = name };
        widget.Raise(new WidgetCreated(widget.Id));
        return widget;
    }

    public void Rename(string name)
    {
        Name = name;
        Raise(new WidgetRenamed(Id));
    }
}

public sealed record WidgetCreated(Guid Id) : INotification;

public sealed record WidgetRenamed(Guid Id) : INotification;

// Records every published domain event so a test can assert the interceptor fired.
public sealed class EventRecorder
{
    private readonly List<INotification> _events = [];

    public IReadOnlyList<INotification> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    public void Add(INotification e)
    {
        lock (_events)
        {
            _events.Add(e);
        }
    }
}

public sealed class WidgetCreatedHandler(EventRecorder recorder) : INotificationHandler<WidgetCreated>
{
    public Task HandleAsync(WidgetCreated notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return Task.CompletedTask;
    }
}

public sealed class WidgetRenamedHandler(EventRecorder recorder) : INotificationHandler<WidgetRenamed>
{
    public Task HandleAsync(WidgetRenamed notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return Task.CompletedTask;
    }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>().HasKey(x => x.Id);
        modelBuilder.Entity<Widget>().Property(x => x.Name).IsRequired();
        modelBuilder.ApplyRaskConventions();
    }
}

// A clock the tests advance by hand to assert CreatedAt/UpdatedAt behavior.
public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
