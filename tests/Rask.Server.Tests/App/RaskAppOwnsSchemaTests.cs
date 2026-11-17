using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.App;

/// <summary>
///     <c>app.Configure(c =&gt; c.Data.OwnsSchema(…))</c>: a <see cref="RaskApp"/> takes over a database whose schema
///     predates its migrations, through the same <see cref="SchemaOwnership"/> a hand-wired host's
///     <c>AddRaskData(…).OwnsSchema(…)</c> takes.
/// </summary>
/// <remarks>
///     The app's own <see cref="LedgerContext"/> over a real SQLite file, every other battery off, so the only thing
///     touching the database is the migration — the migrations are <see cref="OwnsSchemaTests"/>'s.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed class RaskAppOwnsSchemaTests : IDisposable
{
    private const string LedgersTable =
        "CREATE TABLE \"Ledgers\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Ledgers\" PRIMARY KEY AUTOINCREMENT, \"Title\" TEXT NOT NULL);";

    private readonly LedgerDatabase _db = new();

    [Fact]
    public async Task A_database_the_old_migrations_made_is_taken_over_from_Init_without_running_the_baseline_again()
    {
        await _db.Run(LedgersTable
                  + "INSERT INTO \"Ledgers\" (\"Title\") VALUES ('kept'); "
                  + "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL); "
                  + "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20250101000000_Init', '9.0.0');");
        var app = App(c => c.Data.OwnsSchema(s => s.TakesOverFrom("Init").WhenTable("Ledgers")));

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["20250101000000_Init", LedgerBaseline.Id, LedgerMemo.Id], await _db.History());
        Assert.Equal("kept|", await _db.Scalar("SELECT \"Title\" || '|' || COALESCE(\"Memo\", '') FROM \"Ledgers\""));
    }

    [Fact]
    public async Task A_database_that_has_the_table_is_taken_over_even_with_no_history()
    {
        await _db.Run(LedgersTable);
        var app = App(c => c.Data.OwnsSchema(s => s.WhenTable("Ledgers")));

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([LedgerBaseline.Id, LedgerMemo.Id], await _db.History());
    }

    [Fact]
    public async Task A_database_without_the_table_is_made_by_the_migrations_from_the_first()
    {
        await _db.Run("CREATE TABLE \"Unrelated\" (\"Id\" INTEGER NOT NULL);");
        var app = App(c => c.Data.OwnsSchema(s => s.WhenTable("Ledgers")));

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([LedgerBaseline.Id, LedgerMemo.Id], await _db.History());
        Assert.Equal(0L, await _db.Scalar("SELECT COUNT(*) FROM \"Ledgers\""));
    }

    [Fact]
    public async Task Patience_is_the_command_timeout_each_migration_of_the_app_runs_with()
    {
        var witness = new MemoTimeoutWitness();
        var app = App(c => c.Data.OwnsSchema(s => s.Patience(10.Minutes)), witness);

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal((int)10.Minutes.TotalSeconds, witness.AddingTheMemo);
    }

    [Fact]
    public async Task With_MigrateOnStart_off_the_ownership_takes_nothing_over_at_start()
    {
        await _db.Run(LedgersTable);
        var app = App(c =>
        {
            c.MigrateOnStart = false;
            c.Data.OwnsSchema(s => s.WhenTable("Ledgers"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0L, await _db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = '__EFMigrationsHistory'"));
    }

    [Fact]
    public async Task The_setting_turning_MigrateOnStart_off_still_wins_over_an_ownership_left_to_it()
    {
        await _db.Run(LedgersTable);
        var app = App(c => c.Data.OwnsSchema(s => s.WhenTable("Ledgers")), setting: "false");

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0L, await _db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = '__EFMigrationsHistory'"));
    }

    [Fact]
    public void Owning_the_schema_with_the_data_battery_off_refuses_the_start_and_says_why()
    {
        var configure = (RaskAppOptions c) =>
        {
            c.Data.Off();
            c.Data.OwnsSchema(s => s.WhenTable("Ledgers"));
        };

        var refused = Assert.Throws<InvalidOperationException>(() => App(configure));

        Assert.Contains("c.Data.OwnsSchema(…)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("c.Data.Off() leaves the database out of the app", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Owning_the_schema_with_the_mediator_off_refuses_the_start_as_well()
    {
        var configure = (RaskAppOptions c) =>
        {
            c.Cqrs.Off();
            c.Data.OwnsSchema(s => s.WhenTable("Ledgers"));
        };

        var refused = Assert.Throws<InvalidOperationException>(() => App(configure));

        Assert.Contains("c.Cqrs.Off() leaves the app without a database", refused.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _db.Dispose();

    private WebApplication App(Action<RaskAppOptions> configure, MemoTimeoutWitness? witness = null, string? setting = null)
    {
        var app = RaskApp.Create([], b =>
        {
            b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            if (setting is not null)
            {
                b.Configuration["Rask:Database:MigrateOnStart"] = setting;
            }
        });
        app.Configure(c =>
        {
            c.Auth.Off();
            c.Jobs.Off();
            c.Mail.Off();
            c.Cache.Off();
            c.Storage.Off();
            c.Push.Off();
            c.Ops.Off();
            c.Snapshots.Off();
            configure(c);
        });
        app.Services.AddDbContextFactory<LedgerContext>(o =>
        {
            o.UseSqlite(_db.Connection);
            if (witness is not null)
            {
                o.AddInterceptors(witness);
            }
        });
        return app.Build<MinimalApp>();
    }
}
