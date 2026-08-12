using Microsoft.EntityFrameworkCore;
using Rask.Jobs;

namespace Rask.Example.Wasm.Jobs;

/// <summary>One greeting, written by the job handler rather than by the click that queued it.</summary>
public sealed class Greeting
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
///     The app's database. Identical to what this would be on a server, including
///     <c>modelBuilder.AddRaskJobs()</c> — the job and recurring-state tables are the same tables.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Greeting> Greetings => Set<Greeting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskJobs();
    }
}
