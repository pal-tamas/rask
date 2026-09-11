// EF1001: SqliteSqlGenerationHelper is EF-internal. Subclassing it keeps every SQLite behaviour EnsureCreated and
// the queries below depend on, and changes only the two spellings under test.
#pragma warning disable EF1001

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Sqlite.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// The SkipChangeTracking writer builds its INSERT by hand, so it has to spell identifiers and parameters the
// way the provider does. It used to hard-code "…" and @p0 — right on SQLite, PostgreSQL and SQL Server, and a
// string literal to MySQL. These run on SQLite with a generation helper that spells both differently (SQLite
// accepts [x] identifiers and $p0 parameters), so a writer that ignored the provider would be caught here
// rather than on the first MySQL deployment.
[Collection(DataDbCollection.Name)]
public sealed class BulkInsertProviderSpellingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-bulk-spelling-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;

    public BulkInsertProviderSpellingTests()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .ReplaceService<ISqlGenerationHelper, BracketSqlGenerationHelper>()
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    private TestDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();

    [Fact]
    public void The_statement_is_spelled_by_the_providers_generation_helper()
    {
        using var db = NewContext();
        var plan = BulkInsertPlan.For<Widget>(db);

        // Built from the plan's own columns so the assertion is exact rather than a substring match, which a
        // stray "…" anywhere in the statement would otherwise slip past.
        var expected =
            "INSERT INTO [Widgets] (" +
            string.Join(", ", plan.Columns.Select(c => $"[{c.ColumnName}]")) +
            ") VALUES (" +
            string.Join(", ", plan.Columns.Select((_, i) => $"$p{i}")) +
            ");";

        Assert.Equal(expected, plan.CommandText);
        Assert.All(plan.Columns, (c, i) => Assert.Equal($"$p{i}", c.ParameterName));
    }

    [Fact]
    public async Task Rows_written_in_that_spelling_land()
    {
        var widgets = Enumerable.Range(0, 5).Select(i => Widget.Create($"widget-{i}")).ToArray();
        foreach (var widget in widgets)
        {
            widget.ClearDomainEvents();
        }

        await using (var db = NewContext())
        {
            Assert.Equal(5, await db.BulkInsertAsync(widgets, o => o.SkipChangeTracking = true));
        }

        await using var verify = NewContext();
        Assert.Equal(5, await verify.Widgets.CountAsync());
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", true)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", false)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", false)]
    [InlineData("MySql.EntityFrameworkCore", false)]
    [InlineData(null, false)]
    public void Only_SQLite_runs_each_row_synchronously(string? providerName, bool synchronous) =>
        Assert.Equal(synchronous, BulkInsertWriter.ExecutesSynchronously(providerName));

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }

    // Brackets for identifiers, $ for parameters: both valid SQLite, neither what the SQLite provider emits.
    private sealed class BracketSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies)
        : SqliteSqlGenerationHelper(dependencies)
    {
        public override string DelimitIdentifier(string identifier) =>
            $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

        public override void DelimitIdentifier(StringBuilder builder, string identifier) =>
            builder.Append(DelimitIdentifier(identifier));

        // SQLite's helper spells the schema-qualified overloads on their own (SQLite has no schemas), so a
        // stand-in has to override them too or the table name slips back to "…" while the columns do not.
        public override string DelimitIdentifier(string name, string? schema) => DelimitIdentifier(name);

        public override void DelimitIdentifier(StringBuilder builder, string name, string? schema) =>
            builder.Append(DelimitIdentifier(name));

        public override string GenerateParameterName(string name) => "$" + name;

        public override void GenerateParameterName(StringBuilder builder, string name) =>
            builder.Append('$').Append(name);

        public override string GenerateParameterNamePlaceholder(string name) => "$" + name;

        public override void GenerateParameterNamePlaceholder(StringBuilder builder, string name) =>
            builder.Append('$').Append(name);
    }
}
