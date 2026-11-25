using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>A user of an older app, keyed by a number: what its audit rows point at.</summary>
public sealed class LegacyUser
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>
///     A row of an older app's audit table, as Eszaf's <c>Audits</c> is: the table's name, a user by an int foreign key,
///     and the whole row as JSON before and after. A plain class here, mapped by its own context alone: an entity would be
///     mapped into every model of this assembly.
/// </summary>
public sealed class LegacyAudit
{
    private LegacyAudit() { } // EF materialization

    public long Id { get; private set; }

    public string? TableName { get; private set; }

    public int UserId { get; private set; }

    public string? Action { get; private set; }

    public string? OldData { get; private set; }

    public string? NewData { get; private set; }

    public string? PrimaryKey { get; private set; }

    public DateTime DateTime { get; private set; }

    internal static LegacyAudit Of(string table, int user, string action, string? oldData, string? newData, string key, DateTime at) =>
        new() { TableName = table, UserId = user, Action = action, OldData = oldData, NewData = newData, PrimaryKey = key, DateTime = at };

    internal static void Map(EntityTypeBuilder<LegacyAudit> builder)
    {
        builder.ToTable("Audits");
        builder.HasOne<LegacyUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
///     Writes the older app's rows: the aggregate's own row as JSON — its children left out, as the old table never held
///     them — and work for nobody as its system user.
/// </summary>
public sealed class LegacyAudits : AuditFormat<LegacyAudit, int>
{
    public const int SystemUser = 1;

    /// <summary>The last save the format was handed, for a test to read what it was told.</summary>
    public static AuditedWrite<int>? Last { get; private set; }

    public override LegacyAudit Row(AuditedWrite<int> write)
    {
        Last = write;
        return LegacyAudit.Of(
            write.Aggregate.Name,
            write.User ?? SystemUser,
            write.Action.ToString(),
            write.Action == AuditAction.Insert ? null : Json(write.Before),
            write.Action == AuditAction.Delete ? null : Json(write.After),
            Json(write.Key),
            write.At);
    }

    private static string Json(IReadOnlyList<AuditValue> values)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            foreach (var (name, value) in values.Where(v => !v.Name.Contains('[', StringComparison.Ordinal)))
            {
                json.WritePropertyName(name);
                JsonSerializer.Serialize(json, value, value?.GetType() ?? typeof(object));
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>The older app's context: its users, its audit table, and its audit rows named once in the model.</summary>
public sealed class LegacyAuditContext(DbContextOptions<LegacyAuditContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LegacyUser>();
        modelBuilder.AuditAs(new LegacyAudits());
        modelBuilder.Entity<LegacyAudit>(LegacyAudit.Map);
    }
}

/// <summary>
///     <c>m.AuditAs(format)</c>: an <c>Audited</c> aggregate's saves written as the app's own rows, byte for byte as an older
///     table holds them — the whole row before and after, the user as its int key — and Rask's <c>AuditEntry</c> left out.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class AuditedOwnRowTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly string _connection = $"Data Source=legacy-audits-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection? _keeper;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        // The database lives while one connection to it is open.
        _keeper = new SqliteConnection(_connection);
        _keeper.Open();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_keeper is not null)
        {
            await _keeper.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_insert_writes_the_whole_row_as_json_by_the_signed_in_user_s_int_key()
    {
        await using var database = await StartDatabaseAsync();
        using var signedIn = Current.UsePrincipal(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "Test")));

        var tripsheet = await SaveAsync(database, Tripsheet.Write("W-1", cityKm: null, 1.5m, 12.5m));

        var row = Assert.Single(await RowsAsync(database));
        Assert.Equal(("Tripsheet", 7, "Insert", null), (row.TableName, row.UserId, row.Action, row.OldData));
        Assert.Equal($$"""{"Id":{{tripsheet.Id}},"CityKm":null,"Number":"W-1","Version":0,"Weight":1.5}""", row.NewData);
        Assert.Equal($$"""{"Id":{{tripsheet.Id}}}""", row.PrimaryKey);
        Assert.Equal(Noon.UtcDateTime, row.DateTime);
    }

    [Fact]
    public async Task An_update_by_nobody_writes_the_whole_row_before_and_after_as_the_system_user()
    {
        await using var database = await StartDatabaseAsync();
        var id = (await SaveAsync(database, Tripsheet.Write("W-1", cityKm: 40, 1.5m, 12.5m))).Id;

        await Tripsheet.Update(id, w => w.Change(cityKm: null), cancellationToken: Token);

        var row = (await RowsAsync(database))[^1];
        Assert.Equal(("Update", LegacyAudits.SystemUser), (row.Action, row.UserId));
        Assert.Equal($$"""{"Id":{{id}},"CityKm":40,"Number":"W-1","Version":0,"Weight":1.5}""", row.OldData);
        Assert.Equal($$"""{"Id":{{id}},"CityKm":null,"Number":"W-1","Version":1,"Weight":1.5}""", row.NewData);
    }

    [Fact]
    public async Task The_format_is_told_every_row_the_aggregate_holds_and_only_what_changed_as_changed()
    {
        await using var database = await StartDatabaseAsync();
        var id = (await SaveAsync(database, Tripsheet.Write("W-1", cityKm: 40, 1.5m, 12.5m))).Id;

        await Tripsheet.Update(id, w => w.Change(cityKm: 41), cancellationToken: Token);

        var write = LegacyAudits.Last!;
        Assert.Equal(["CityKm", "Version"], write.Changed.Order(StringComparer.Ordinal));
        Assert.Contains(new AuditValue("Legs[0].Litres", 12.5m), write.Before);
        Assert.Contains(new AuditValue("Legs[0].Litres", 12.5m), write.After);
        Assert.Equal(typeof(Tripsheet), write.Aggregate);
    }

    [Fact]
    public async Task A_delete_by_a_job_writes_the_row_it_held_by_the_user_the_job_runs_for()
    {
        await using var database = await StartDatabaseAsync();
        var id = (await SaveAsync(database, Tripsheet.Write("W-1", cityKm: 40, 1.5m))).Id;

        using (Current.UseUser(TenantNumber.ToGuid(7)))
        {
            await Tripsheet.Delete(id, cancellationToken: Token);
        }

        var row = (await RowsAsync(database))[^1];
        Assert.Equal(("Delete", 7, null), (row.Action, row.UserId, row.NewData));
        Assert.Equal($$"""{"Id":{{id}},"CityKm":40,"Number":"W-1","Version":0,"Weight":1.5}""", row.OldData);
    }

    [Fact]
    public async Task Rask_s_own_entries_are_left_out_of_the_model_that_names_the_app_s_rows()
    {
        await using var database = await StartDatabaseAsync();

        Assert.Null(database.Context.Model.FindEntityType(typeof(AuditEntry)));
        Assert.Equal("Audits", database.Context.Model.FindEntityType(typeof(LegacyAudit))!.GetTableName());
    }

    [Fact]
    public async Task A_row_format_registered_as_a_service_is_refused_naming_the_model_line_that_maps_it()
    {
        await using var database = await TestDatabase.Start(
            o => o.UseSqlite(_connection), TimeProvider.System, s => s.AddSingleton<AuditFormat>(new LegacyAudits()), Token);
        database.Context.Add(Tripsheet.Write("W-1", cityKm: null, 1.5m));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.Contains("m.AuditAs(new LegacyAudits())", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<Tripsheet> SaveAsync(TestDatabase database, Tripsheet tripsheet)
    {
        database.Context.Add(tripsheet);
        await database.Context.SaveChangesAsync(Token);
        database.Context.ChangeTracker.Clear();
        return tripsheet;
    }

    private static async Task<List<LegacyAudit>> RowsAsync(TestDatabase database) =>
        await database.Context.Set<LegacyAudit>().AsNoTracking().OrderBy(a => a.Id).ToListAsync(Token);

    private async Task<TestDatabase<LegacyAuditContext>> StartDatabaseAsync()
    {
        var database = await TestDatabase.Start<LegacyAuditContext>(o => o.UseSqlite(_connection), new FakeClock(Noon), cancellationToken: Token);
        database.Context.AddRange(new LegacyUser { Id = LegacyAudits.SystemUser, Name = "system" }, new LegacyUser { Id = 7, Name = "Ada" });
        await database.Context.SaveChangesAsync(Token);
        database.Context.ChangeTracker.Clear();
        return database;
    }
}
