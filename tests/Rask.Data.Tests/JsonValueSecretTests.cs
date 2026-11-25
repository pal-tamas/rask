using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

public sealed partial record WellKey(string Value) : Text(Value, MaxLength: 200);

public sealed record WellPair(string FileId, int DbId);

/// <summary>A value object holding a list and a secret: kept whole in one JSON column.</summary>
public sealed record WellSettings
{
    private WellSettings() { }

    public WellSettings(string provider, Secret<WellKey>? key, ValueList<WellPair> pairs) => (Provider, Key, Pairs) = (provider, key, pairs);

    public string Provider { get; private init; } = "";

    public Secret<WellKey>? Key { get; private init; }

    public ValueList<WellPair> Pairs { get; private init; } = [];
}

public sealed class WellPartner : Aggregate<Guid>
{
    private WellPartner() { }

    public string Name { get; private set; } = "";

    public ValueList<string> Codes { get; private set; } = [];

    public ValueList<WellPair> Pairs { get; private set; } = [];

    public Secret<string>? Token { get; private set; }

    public WellSettings? Settings { get; private set; }

    public static WellPartner Called(string name) => new() { Id = Guid.NewGuid(), Name = name };

    public void Code(params string[] codes) => Codes = [.. codes];

    public void Pair(params WellPair[] pairs) => Pairs = [.. pairs];

    public void Connect(Change<string> token) => Token = Secret.Applied(token, Token);

    public void Settle(WellSettings settings) => Settings = settings;

    public static void Configure(EntityTypeBuilder<WellPartner> b) => b.Json(p => p.Settings, column: "LocalSettings");
}

/// <summary>
///     A value list is a column of values, a value object holding one is kept whole as JSON with <c>b.Json(…)</c>, and a
///     secret — on the row or inside that JSON — is stored as its ciphertext and read back as what it held.
/// </summary>
public sealed class JsonValueSecretTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    // The test's own secrets, for its flow alone: the constructor runs in the flow the test does.
    public JsonValueSecretTests() => SecretKeeping.UseInFlow(new ReversedSecrets());

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_value_object_kept_as_json_holds_its_list_and_its_secret_in_one_column()
    {
        await using var database = await StartDatabaseAsync();
        var partner = WellPartner.Called("Derby");
        partner.Settle(new WellSettings("ta", Secret.Of(WellKey.Create("k-123")), [new WellPair("7", 1), new WellPair("8", 2)]));
        database.Context.Add(partner);
        await database.Context.SaveChangesAsync(Cancel);

        var stored = await Column(partner.Id, "LocalSettings");
        await using var fresh = Fresh();
        var loaded = await fresh.Set<WellPartner>().SingleAsync(Cancel);

        Assert.Contains("\"Pairs\":[", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("k-123", stored, StringComparison.Ordinal);
        Assert.Equal("k-123", loaded.Settings!.Key!.Reveal().Value);
        Assert.Equal([("7", 1), ("8", 2)], loaded.Settings.Pairs.Select(p => (p.FileId, p.DbId)));
        Assert.Equal(partner.Settings, loaded.Settings);
    }

    [Fact]
    public async Task A_secret_on_the_row_is_its_ciphertext_and_an_empty_change_keeps_it()
    {
        await using var database = await StartDatabaseAsync();
        var partner = WellPartner.Called("Derby");
        partner.Connect(Change<string>.Set("s3cr3t"));
        database.Context.Add(partner);
        await database.Context.SaveChangesAsync(Cancel);

        partner.Connect(Change<string>.Keep);
        await database.Context.SaveChangesAsync(Cancel);
        var stored = await Column(partner.Id, "Token");
        await using var fresh = Fresh();
        var loaded = await fresh.Set<WellPartner>().SingleAsync(Cancel);

        Assert.Equal(new ReversedSecrets().Protect("s3cr3t"), stored);
        Assert.Equal("s3cr3t", loaded.Token!.Reveal());
        Assert.Equal("••••", loaded.Token.ToString());
    }

    [Fact]
    public async Task Value_lists_are_columns_filtered_in_the_database()
    {
        await using var database = await StartDatabaseAsync();
        var derby = WellPartner.Called("Derby");
        derby.Code("A1", "B2");
        derby.Pair(new WellPair("7", 1));
        var other = WellPartner.Called("Other");
        other.Code("C3");
        database.Context.AddRange(derby, other);
        await database.Context.SaveChangesAsync(Cancel);

        await using var fresh = Fresh();
        var byCode = await fresh.Set<WellPartner>().Where(p => p.Codes.Contains("B2")).Select(p => p.Name).SingleAsync(Cancel);
        var byPair = await fresh.Set<WellPartner>().Where(p => p.Pairs.Any(x => x.FileId == "7")).Select(p => p.Name).SingleAsync(Cancel);
        var loaded = await fresh.Set<WellPartner>().SingleAsync(p => p.Name == "Derby", Cancel);

        Assert.Equal("Derby", byCode);
        Assert.Equal("Derby", byPair);
        Assert.Equal(ValueList.Of(["A1", "B2"]), loaded.Codes);
    }

    [Fact]
    public async Task A_changed_value_list_is_saved_and_an_unchanged_one_writes_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var partner = WellPartner.Called("Derby");
        partner.Code("A1");
        database.Context.Add(partner);
        await database.Context.SaveChangesAsync(Cancel);

        await using var fresh = Fresh();
        var loaded = await fresh.Set<WellPartner>().SingleAsync(Cancel);
        loaded.Code("A1");
        fresh.ChangeTracker.DetectChanges();
        var unchanged = fresh.Entry(loaded).State;
        loaded.Code("A1", "Z9");
        await fresh.SaveChangesAsync(Cancel);

        Assert.Equal(EntityState.Unchanged, unchanged);
        Assert.Equal("[\"A1\",\"Z9\"]", await Column(partner.Id, "Codes"));
    }

    [Fact]
    public void The_read_face_carries_neither_the_secret_nor_the_json_value_object()
    {
        var face = typeof(WellPartnerRead);

        Assert.NotNull(face.GetProperty(nameof(WellPartner.Name)));
        Assert.Null(face.GetProperty(nameof(WellPartner.Token)));
        Assert.Null(face.GetProperty(nameof(WellPartner.Settings)));
    }

    private async Task<string> Column(Guid id, string column)
    {
        string table;
        await using (var db = Fresh())
        {
            table = db.Model.FindEntityType(typeof(WellPartner))!.GetTableName()!;
        }

        await using var connection = new SqliteConnection(_sqlite.ConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{column}\" FROM \"{table}\" WHERE \"Id\" = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        return (string)(await command.ExecuteScalarAsync(Cancel))!;
    }

    private RaskDbContext Fresh() =>
        new(OptionsLayout.LikeAddDbContext<RaskDbContext>().UseSqlite(_sqlite.ConnectionString).Options);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));

    // Reversible and visibly not the plaintext: what a real key ring is to the test, without one.
    private sealed class ReversedSecrets : ISecrets
    {
        public string Protect(string plaintext) => "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plaintext.Reverse().ToArray())));

        public string Unprotect(string stored) => new(Encoding.UTF8.GetString(Convert.FromBase64String(stored["enc:".Length..])).Reverse().ToArray());
    }
}
