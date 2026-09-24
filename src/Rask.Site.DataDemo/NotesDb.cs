using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Site.DataDemo;

/// <summary>
///     The write context. A Rask server app writes none — the host maps every aggregate for it — but this browser
///     app wires Rask.Data by hand, so it names its one aggregate and the index a search reads.
/// </summary>
public sealed class NotesDb(DbContextOptions<NotesDb> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The full-text index over both columns: an FTS5 table kept current by triggers, created with the schema.
        modelBuilder.Entity<Note>().HasFullTextSearch(n => new { n.Title, n.Body });

        // Timestamps, the Version concurrency token and the aggregate's own key — always last.
        modelBuilder.ApplyRaskConventions(this);
    }
}
