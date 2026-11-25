using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>The app's own kind of gate.</summary>
public enum TruckModule
{
    Tachograph,
}

/// <summary>A driver whose card only the tachograph module may write.</summary>
public sealed class Trucker : Aggregate<Guid, Versioned>
{
    private Trucker() { }

    public string Name { get; private set; } = "";

    public string? Card { get; private set; }

    public static Trucker Named(string name, string? card) => new() { Id = Guid.NewGuid(), Name = name, Card = card };

    public void Describe(string name, string? card) => (Name, Card) = (name, card);

    public static void Configure(EntityTypeBuilder<Trucker> builder) => builder.Gated(h => h.Card, TruckModule.Tachograph);
}

/// <summary>
/// F-35: a member gated with <c>b.Gated(…)</c> is written only while its gate is open — a save with it shut keeps the
/// stored value, an add stores none — and a gate nothing says anything about refuses the save.
/// </summary>
public sealed class GatedTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    private bool _open = true;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task With_the_gate_open_the_member_is_written()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync("C-1");

        await ChangeAsync(id, h => h.Describe("Ada", "C-2"));

        Assert.Equal("C-2", (await Trucker.Find(id, Token))!.Card);
    }

    [Fact]
    public async Task With_the_gate_shut_a_change_keeps_the_stored_value_and_writes_the_rest()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync("C-1");
        _open = false;

        await ChangeAsync(id, h => h.Describe("Eve", null));

        var stored = (await Trucker.Find(id, Token))!;
        Assert.Equal("C-1", stored.Card);
        Assert.Equal("Eve", stored.Name);
    }

    [Fact]
    public async Task With_the_gate_shut_an_added_row_stores_nothing_there()
    {
        await using var database = await StartDatabaseAsync();
        _open = false;

        var id = await SeedAsync("C-9");

        Assert.Null((await Trucker.Find(id, Token))!.Card);
    }

    [Fact]
    public async Task A_gate_nothing_says_anything_about_refuses_the_save()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: Token);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync("C-1"));

        Assert.Contains("AddRaskGate<TruckModule>", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_asks_whether_a_gated_member_is_shut_and_an_ungated_one_never_is()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync("C-1");

        var whileOpen = GeneratedGates.IsShut(typeof(Trucker), nameof(Trucker.Card));
        _open = false;
        var whileShut = GeneratedGates.IsShut(typeof(Trucker), nameof(Trucker.Card));

        Assert.Equal((false, true), (whileOpen, whileShut));
        Assert.False(GeneratedGates.IsShut(typeof(Trucker), nameof(Trucker.Name)));
    }

    [Fact]
    public async Task A_read_face_hands_back_no_gated_member_while_its_gate_is_shut()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync("C-1");

        var open = await Trucker.Where(t => t.Name == "Ada").Single(Token);
        _open = false;
        var shut = await Trucker.Where(t => t.Name == "Ada");

        Assert.Equal("C-1", open.Card);
        Assert.Null(Assert.Single(shut).Card);
        Assert.Equal("Ada", shut[0].Name);
    }

    [Fact]
    public void The_gated_member_is_declared_at_startup_before_any_model_is_built()
    {
        var declared = GatedMembers.Of(typeof(Trucker)).Select(static m => m.Key);

        Assert.Contains(nameof(Trucker.Card), declared);
    }

    private static async Task<Guid> SeedAsync(string card)
    {
        var trucker = Trucker.Named("Ada", card);
        await trucker.Save(cancellationToken: Token);
        return trucker.Id;
    }

    private static async Task ChangeAsync(Guid id, Action<Trucker> change)
    {
        var trucker = (await Trucker.Find(id, Token))!;
        change(trucker);
        await trucker.Save(cancellationToken: Token);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(
            o => o.UseSqlite(_sqlite.ConnectionString),
            services: s => s.AddRaskGate<TruckModule>((_, _) => _open));
}
