using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK177: app code written for one database — a provider's API, EF.Functions, raw SQL, SQL in the model — outside
/// migrations, generated code and the call that selects the provider.
/// </summary>
public sealed class ProviderSpecificCodeAnalyzerTests
{
    private static string App(string body, string members = "") => $$"""
        using System;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Microsoft.EntityFrameworkCore.Migrations;
        namespace Shop;
        public sealed class Note
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = "";
            public decimal Price { get; set; }
            public DateTime At { get; set; }
            public DateTime? Gone { get; set; }
        }
        public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
        {
            public DbSet<Note> Notes => Set<Note>();
        }
        public static class Code
        {
            {{members}}
            public static async Task Run(ShopDb db, ModelBuilder model, EntityTypeBuilder<Note> b, DbContextOptionsBuilder<ShopDb> o)
            {
                {{body}}
                await Task.CompletedTask;
            }
        }
        """;

    private static Task<IReadOnlyList<Diagnostic>> Analyzed(string source, string path = "Source.cs") =>
        ModelStateAnalyzerHarness.RunAsync(new ProviderSpecificCodeAnalyzer(), source, path: path);

    [Theory]
    [InlineData("_ = db.Notes.Where(n => EF.Functions.Like(n.Title, \"%a%\"));", "'EF.Functions.Like'", "Contains, StartsWith or EndsWith")]
    [InlineData("_ = db.Notes.OrderBy(n => EF.Functions.Collate(n.Title, \"Hungarian_100_CI_AS\"));", "'EF.Functions.Collate'", "InTextOrder()")]
    [InlineData("_ = db.Notes.Where(n => EF.Functions.Collate(n.Title, \"Latin1_General_100_CI_AI\").Contains(\"a\"));", "'EF.Functions.Collate'", "x.Loosely().Contains(typed)")]
    [InlineData("_ = db.Notes.Select(n => EF.Functions.DateDiffDay(n.At, DateTime.UtcNow));", "'EF.Functions.DateDiffDay' is written for SQL Server", "end - start")]
    [InlineData("_ = db.Notes.FromSqlRaw(\"SELECT * FROM Notes\");", "'FromSqlRaw'", "a LINQ query over the model")]
    [InlineData("_ = db.Notes.FromSql($\"SELECT * FROM Notes WITH (UPDLOCK)\");", "'FromSql'", "ForUpdate()")]
    [InlineData("await db.Database.ExecuteSqlRawAsync(\"EXEC sp_getapplock @Resource = 'x'\");", "'ExecuteSqlRawAsync'", "Db.Lock(name)")]
    [InlineData("await db.Database.ExecuteSqlAsync($\"DELETE FROM Notes\");", "'ExecuteSqlAsync'", "ExecuteUpdate/ExecuteDelete")]
    [InlineData("_ = db.Database.SqlQueryRaw<int>(\"SELECT 1\");", "'SqlQueryRaw'", "a LINQ query")]
    [InlineData("b.Property(n => n.Price).HasColumnType(\"decimal(18, 9)\");", "'HasColumnType'", "Scale: 2")]
    [InlineData("b.Metadata.SetContainerColumnType(\"nvarchar(max)\");", "'SetContainerColumnType'", "leave the JSON column's type to Rask")]
    [InlineData("model.UseCollation(\"Hungarian_100_CI_AS\");", "'UseCollation'", "OrderTextBy")]
    [InlineData("b.Property(n => n.Title).UseCollation(\"Hungarian_100_CI_AS\");", "'UseCollation'", "OrderTextBy")]
    [InlineData("b.Property(n => n.At).HasDefaultValueSql(\"SYSUTCDATETIME()\");", "'HasDefaultValueSql'", "b.Default(")]
    [InlineData("b.Property(n => n.Price).HasDefaultValueSql(\"0\");", "'HasDefaultValueSql'", "b.StoredDefault(")]
    [InlineData("b.Property(n => n.Title).HasComputedColumnSql(\"UPPER([Title])\");", "'HasComputedColumnSql'", "b.Computed(")]
    [InlineData("b.HasIndex(n => n.Title).HasFilter(\"[Gone] IS NULL\");", "'HasFilter'", ".Where(x =>")]
    [InlineData("SqlServerIndexBuilderExtensions.IncludeProperties(b.HasIndex(n => n.Title), n => n.Price);", "'IncludeProperties' is written for SQL Server", ".Covering(")]
    [InlineData("b.HasIndex(n => n.Title).IsClustered();", "'IsClustered' is written for SQL Server", "UseRaskDatabase(sp)")]
    public async Task Code_written_for_one_database_is_RASK177_naming_the_neutral_way(string body, string named, string replacement)
    {
        var source = App(body);

        var diagnostics = await Analyzed(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("RASK177", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains(named, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(replacement, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("try { await db.SaveChangesAsync(); } catch (Microsoft.Data.SqlClient.SqlException) { }", "'SqlException' is written for SQL Server")]
    [InlineData("try { await db.SaveChangesAsync(); } catch (Exception e) when (e.InnerException is Npgsql.PostgresException) { }", "'PostgresException' is written for PostgreSQL")]
    [InlineData("using var c = new Microsoft.Data.Sqlite.SqliteConnection(\"Data Source=:memory:\");", "'SqliteConnection' is written for SQLite")]
    public async Task A_drivers_type_named_in_app_code_is_RASK177(string body, string named)
    {
        var source = App(body);

        var diagnostics = await Analyzed(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains(named, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("o.UseSqlServer(\"Server=.\", sql => sql.UseCompatibilityLevel(160).EnableRetryOnFailure());")]
    [InlineData("o.UseNpgsql(\"Host=.\");")]
    [InlineData("o.UseSqlite(\"Data Source=app.db\");")]
    [InlineData("_ = db.Notes.Where(n => n.Title.Contains(\"a\")).OrderBy(n => n.Title);")]
    [InlineData("b.Property(n => n.Price).HasPrecision(18, 2); b.HasIndex(n => n.Title).HasFilter(null); b.Property(n => n.Title).HasMaxLength(200);")]
    public async Task Selecting_the_provider_and_neutral_code_are_not_RASK177(string body)
    {
        var source = App(body);

        var diagnostics = await Analyzed(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_migration_is_written_for_its_database_and_is_not_RASK177()
    {
        var source = App("", """
            }
            public sealed class AddNotes : Migration
            {
                protected override void Up(MigrationBuilder m)
                {
                    m.Sql("EXEC sp_getapplock @Resource = 'x'");
                    m.AlterColumn<decimal>("Price", "Notes").Annotation("SqlServer:Identity", "1, 1");
                }
            """);

        var diagnostics = await Analyzed(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_file_under_a_Migrations_folder_is_not_RASK177()
    {
        var source = App("_ = db.Notes.FromSqlRaw(\"SELECT * FROM Notes\");");

        var diagnostics = await Analyzed(source, path: "/app/Data/Migrations/Seed.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SQL_sent_outside_a_migration_is_RASK177()
    {
        var source = App("new MigrationBuilder(\"x\").Sql(\"SELECT 1\");");

        var diagnostics = await Analyzed(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Sql'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_code_for_one_database()
    {
        var strict = StrictConfig.Severity("RASK177");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ProviderSpecificCodeAnalyzer(),
            App("_ = db.Notes.FromSqlRaw(\"SELECT * FROM Notes\");"),
            options => options.WithSpecificDiagnosticOptions([new("RASK177", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
    }
}
