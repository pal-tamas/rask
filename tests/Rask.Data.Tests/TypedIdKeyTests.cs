using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// The one line each, as an app declares them (the Rask package completes them; this project does not run that
// generator, and the model needs nothing from it): the model registry converts each to its value.
public readonly partial record struct TerminalId(int Value);

public readonly partial record struct BarrelId(Guid Value);

// An id over an int keys an identity column: the store numbers the terminal, as it would an int key.
public sealed class Terminal : Aggregate<TerminalId, Model>
{
    private Terminal() { }

    public string Name { get; private set; } = "";

    public static Terminal Open(string name) => new() { Name = name };
}

// An id over a Guid is the application's, made in the factory — and held by another aggregate, it is the same type.
public sealed class Barrel : Aggregate<BarrelId>
{
    private Barrel() { }

    public TerminalId Terminal { get; private set; }

    public static Barrel At(TerminalId terminal) => new() { Id = new BarrelId(Guid.CreateVersion7()), Terminal = terminal };
}

public sealed class TypedIdKeyTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));

    [Fact]
    public async Task An_int_typed_id_key_is_numbered_by_the_store_on_insert()
    {
        await using var database = await StartDatabaseAsync();
        var north = Terminal.Open("North");
        var south = Terminal.Open("South");

        database.Context.AddRange(north, south);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(north.Id.Value > 0);
        Assert.NotEqual(north.Id, south.Id);
        Assert.Equal("South", (await database.Load<Terminal>(south.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task The_generated_create_leaves_an_int_typed_id_key_to_the_store()
    {
        await using var database = await StartDatabaseAsync();

        var terminal = await Terminal.Create(new TerminalModel { Name = "East" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(terminal.Id.Value > 0);
        Assert.DoesNotContain(
            typeof(TerminalModelExtensions).GetMethods(),
            m => m.Name == "Create" && m.GetParameters()[0].ParameterType == typeof(TerminalId));
    }

    [Fact]
    public async Task The_key_convention_numbers_an_int_typed_id_and_leaves_a_Guid_one_to_the_application()
    {
        await using var database = await StartDatabaseAsync();

        ValueGenerated Of<TEntity>() =>
            database.Context.Model.FindEntityType(typeof(TEntity))!.FindPrimaryKey()!.Properties.Single().ValueGenerated;

        Assert.Equal(ValueGenerated.OnAdd, Of<Terminal>());
        Assert.Equal(ValueGenerated.Never, Of<Barrel>());
    }

    [Fact]
    public async Task A_Guid_typed_id_in_a_text_column_is_written_lower_case_and_read_back()
    {
        await using var database = await StartDatabaseAsync();
        var terminal = Terminal.Open("West");
        database.Context.Add(terminal);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var barrel = Barrel.At(terminal.Id);

        database.Context.Add(barrel);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(barrel.Id.Value.ToString(), await StoredIdOf<Barrel>(database));
        var loaded = (await database.Load<Barrel>(barrel.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(terminal.Id, loaded.Terminal);
        Assert.Equal(barrel.Id, await database.Context.Set<Barrel>().AsNoTracking().Where(c => c.Id == barrel.Id).Select(c => c.Id)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    // The column as the database holds it — not as EF Core reads it back, which would parse either case.
    private static async Task<string?> StoredIdOf<TEntity>(TestDatabase database)
    {
        var table = database.Context.Model.FindEntityType(typeof(TEntity))!.GetTableName();
        DbConnection connection = database.Context.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Id\" FROM \"{table}\"";
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
