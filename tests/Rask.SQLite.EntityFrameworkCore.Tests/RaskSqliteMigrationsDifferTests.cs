using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>
/// <c>UseRaskSqlite</c> writes a context's migrations through Rask's differ, which fills a collection of value objects
/// turned NOT NULL with <c>[]</c> rather than EF Core's <c>{}</c> — beside, not instead of, its own SQL generator.
/// </summary>
public sealed class RaskSqliteMigrationsDifferTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Use_rask_sqlite_writes_migrations_through_rasks_differ_and_keeps_its_generator(bool strict)
    {
        using var context = new EmptyContext(new DbContextOptionsBuilder<EmptyContext>()
            .UseRaskSqliteAt("Data Source=:memory:", o => o.StrictTables = strict)
            .Options);

        var differ = context.GetService<IMigrationsModelDiffer>();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        Assert.Equal("RaskMigrationsModelDiffer", differ.GetType().Name);
        Assert.StartsWith("RaskSqlite", generator.GetType().Name, StringComparison.Ordinal);
    }

    private sealed class EmptyContext(DbContextOptions<EmptyContext> options) : DbContext(options);
}
