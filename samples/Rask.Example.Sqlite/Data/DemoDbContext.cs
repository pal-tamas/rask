using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Example.Sqlite.Data;

// A trivial context whose only job is to give the concurrent-writes demo something to write to.
// Resolved through IDbContextFactory (see Program.cs) so each writer gets its own short-lived context.
public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options) : DbContext(options)
{
    public DbSet<WriteLog> WriteLogs => Set<WriteLog>();

    public DbSet<Reading> Readings => Set<Reading>();
}

// One row per successful concurrent write — the demo counts these to prove every writer committed.
public sealed class WriteLog
{
    public int Id { get; set; }

    public string Note { get; set; } = string.Empty;
}

// The bulk-import demo's row: a sensor reading, the shape that actually arrives in bulk. It derives from
// Entity<Guid> so the import also shows Rask.Data's audit stamps being applied on the fast path, where no
// interceptor runs to apply them. The key matters here — it is assigned by Create, on the client. WriteLog
// above has an int key, which SQLite assigns as the rowid, and SkipChangeTracking refuses that shape by
// design: the value only exists after the insert that the change tracker is there to read it back from.
public sealed class Reading : Entity<Guid>
{
    private Reading() { } // EF materialization

    public string Sensor { get; private set; } = string.Empty;

    public double Value { get; private set; }

    public static Reading Create(string sensor, double value) =>
        new() { Id = Guid.NewGuid(), Sensor = sensor, Value = value };
}
