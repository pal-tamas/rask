using Microsoft.EntityFrameworkCore;

namespace Rask.Example.Sqlite.Data;

// A trivial context whose only job is to give the concurrent-writes demo something to write to.
// Resolved through IDbContextFactory (see Program.cs) so each writer gets its own short-lived context.
public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options) : DbContext(options)
{
    public DbSet<WriteLog> WriteLogs => Set<WriteLog>();
}

// One row per successful concurrent write — the demo counts these to prove every writer committed.
public sealed class WriteLog
{
    public int Id { get; set; }

    public string Note { get; set; } = string.Empty;
}
