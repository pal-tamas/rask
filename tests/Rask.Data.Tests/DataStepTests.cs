using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.Data.Tests;

// A migration's data step — `b.Data(async () => …)` — runs in C# once the schema is migrated, once, recorded in
// __RaskDataSteps; and every provider writes it into the migration's SQL as nothing but a comment.
public sealed class DataStepTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_data_step_runs_after_the_schema_is_migrated_and_is_recorded()
    {
        await using var database = await OpenAsync<SteppedContext>();
        using var log = StepLog.Begin();
        await database.Context.Database.MigrateAsync(Token);

        var ran = await DataSteps.RunPendingAsync(database.Context, Token);

        Assert.Equal([AddSteppedNotes.Id], ran);
        Assert.Equal(["filled the notes"], log.Steps);
        Assert.Equal([AddSteppedNotes.Id], await RecordedAsync(database.Context));
    }

    [Fact]
    public async Task A_data_step_that_has_run_does_not_run_again()
    {
        await using var database = await OpenAsync<SteppedContext>();
        await database.Context.Database.MigrateAsync(Token);
        await DataSteps.RunPendingAsync(database.Context, Token);
        using var log = StepLog.Begin();

        var ran = await DataSteps.RunPendingAsync(database.Context, Token);

        Assert.Empty(ran);
        Assert.Empty(log.Steps);
    }

    [Fact]
    public async Task A_migration_not_yet_applied_has_its_step_wait_for_its_schema()
    {
        await using var database = await OpenAsync<SteppedContext>();
        using var log = StepLog.Begin();
        await database.Context.GetService<IMigrator>().MigrateAsync(CreateSteppedNotes.Id, cancellationToken: Token);

        var ran = await DataSteps.RunPendingAsync(database.Context, Token);

        Assert.Empty(ran);
        Assert.Empty(log.Steps);
    }

    [Fact]
    public async Task Migrations_that_declare_no_step_leave_the_database_without_the_record()
    {
        await using var database = await OpenAsync<PlainNotesContext>();
        await database.Context.Database.MigrateAsync(Token);

        var ran = await DataSteps.RunPendingAsync(database.Context, Token);

        Assert.Empty(ran);
        Assert.Equal(0, await database.Context.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE name = '{DataSteps.Table}'").SingleAsync(Token));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("sqlserver")]
    public void Every_provider_writes_a_data_step_into_the_migrations_sql_as_a_comment_alone(string provider)
    {
        var builder = OptionsLayout.LikeAddDbContext<SteppedContext>();
        _ = provider == "sqlite" ? builder.UseSqlite("Data Source=:memory:") : builder.UseSqlServer("Server=nowhere;Database=none");
        using var context = new SteppedContext(builder.Options);

        var commands = context.GetService<IMigrationsSqlGenerator>().Generate([DataStep.Holding(() => Task.CompletedTask)]);

        Assert.All(commands, c => Assert.StartsWith("--", c.CommandText.Trim(), StringComparison.Ordinal));
    }

    private static async Task<List<string>> RecordedAsync(DbContext context) =>
        await context.Database.SqlQueryRaw<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"{DataSteps.Table}\"").ToListAsync(Token);

    // A named in-memory database, alive while the returned connection is open.
    private static async Task<Opened<TContext>> OpenAsync<TContext>()
        where TContext : DbContext
    {
        var keeper = new SqliteConnection($"Data Source=steps-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await keeper.OpenAsync(Token);
        var options = new DbContextOptionsBuilder<TContext>().UseSqlite(keeper.ConnectionString).Options;
        return new Opened<TContext>(keeper, (TContext)Activator.CreateInstance(typeof(TContext), options)!);
    }

    private sealed class Opened<TContext>(SqliteConnection keeper, TContext context) : IAsyncDisposable
        where TContext : DbContext
    {
        public TContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await keeper.DisposeAsync();
        }
    }
}

/// <summary>What the data steps below did, in the flow of the test that ran them.</summary>
internal sealed class StepLog : IDisposable
{
    private static readonly AsyncLocal<StepLog?> Current = new();

    public List<string> Steps { get; } = [];

    public static StepLog Begin() => Current.Value = new StepLog();

    public static Task Note(string step)
    {
        Current.Value?.Steps.Add(step);
        return Task.CompletedTask;
    }

    public void Dispose() => Current.Value = null;
}

public sealed class SteppedNote
{
    public int Id { get; set; }

    public string Text { get; set; } = "";
}

/// <summary>A context whose second migration declares a data step.</summary>
public sealed class SteppedContext(DbContextOptions<SteppedContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<SteppedNote>().ToTable("SteppedNotes");
}

/// <summary>The same table, its migrations declaring no step.</summary>
public sealed class PlainNotesContext(DbContextOptions<PlainNotesContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<SteppedNote>().ToTable("SteppedNotes");
}

[DbContext(typeof(SteppedContext))]
[Migration(Id)]
public sealed class CreateSteppedNotes : Migration
{
    public const string Id = "20261124000000_CreateSteppedNotes";

    protected override void Up(MigrationBuilder migrationBuilder) => SteppedNotesSchema.Create(migrationBuilder);
}

[DbContext(typeof(SteppedContext))]
[Migration(Id)]
public sealed class AddSteppedNotes : Migration
{
    public const string Id = "20261124000001_AddSteppedNotes";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Data(() => StepLog.Note("filled the notes"));
}

[DbContext(typeof(PlainNotesContext))]
[Migration("20261124000000_CreateSteppedNotes")]
public sealed class CreatePlainNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => SteppedNotesSchema.Create(migrationBuilder);
}

internal static class SteppedNotesSchema
{
    public static void Create(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "SteppedNotes",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Text = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_SteppedNotes", x => x.Id));

    public static void Snapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "11.0.0");
        modelBuilder.Entity("Rask.Data.Tests.SteppedNote", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("Text").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("SteppedNotes");
        });
    }
}

[DbContext(typeof(SteppedContext))]
public sealed class SteppedContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => SteppedNotesSchema.Snapshot(modelBuilder);
}

[DbContext(typeof(PlainNotesContext))]
public sealed class PlainNotesContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => SteppedNotesSchema.Snapshot(modelBuilder);
}
