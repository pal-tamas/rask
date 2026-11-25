using System.Text;
using Microsoft.EntityFrameworkCore;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.Storage.Tests;

/// <summary>An app's context, mapping the stored-file table as a RaskApp's does.</summary>
public sealed class CabinetContext(DbContextOptions<CabinetContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskStorage();
    }
}

/// <summary>A <c>TestDatabase</c> over a model that maps the stored-file table stands file storage in with its fake.</summary>
public sealed class FilesTestDatabaseTests : IDisposable
{
    private readonly InMemorySqlite _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_save_with_no_host_is_kept_by_the_database_s_files_fake()
    {
        await using var database = await TestDatabase.Start<CabinetContext>(o => o.UseSqlite(_db.ConnectionString), cancellationToken: Ct);

        await Files.Save(new MemoryStream(Encoding.UTF8.GetBytes("hello")), "note.txt", Ct);

        database.Files.Saved().Named("note.txt").Once();
    }
}
