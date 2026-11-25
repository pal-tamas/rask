using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A provider whose key comes from a sequence starting at 1000, known before its insert.</summary>
public sealed class SequencedProvider : Aggregate<int, Versioned>
{
    private SequencedProvider() { }

    public string Name { get; private set; } = "";

    public string Code { get; private set; } = "";

    public static SequencedProvider Named(string name) => new() { Name = name };

    // An existing row, imported with the key it already had.
    public static SequencedProvider Imported(int id, string name) => new() { Id = id, Name = name };

    // What the key used to need a second save for.
    public void TakeCode() => Code = "p" + Id;

    public static void Configure(EntityTypeBuilder<SequencedProvider> builder) =>
        builder.Property(p => p.Id).FromSequence("SequencedProviders", start: 1000);
}

/// <summary>
/// F-34: <c>FromSequence</c> hands out the key as the row is added — before its insert — from the provider's sequence,
/// or on SQLite from Rask's own table, started above any key the table already holds.
/// </summary>
public sealed class FromSequenceTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task The_key_is_never_generated_by_the_store_and_carries_no_annotation_into_the_model()
    {
        await using var database = await StartDatabaseAsync();

        var key = database.Context.Model.FindEntityType(typeof(SequencedProvider))!.FindProperty("Id")!;

        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, key.ValueGenerated);
        Assert.Null(key.FindAnnotation(SequenceBuilderExtensions.Annotation));
        Assert.Empty(database.Context.Model.GetSequences());
    }

    [Fact]
    public async Task Added_rows_take_the_sequence_from_its_start_before_their_insert()
    {
        await using var database = await StartDatabaseAsync();
        var first = SequencedProvider.Named("MOL");
        var second = SequencedProvider.Named("OMV");

        database.Context.Add(first);
        first.TakeCode();
        database.Context.Add(second);
        await database.Context.SaveChangesAsync(Token);

        Assert.Equal([1000, 1001], new[] { first.Id, second.Id });
        Assert.Equal("p1000", (await SequencedProvider.Find(1000, Token))!.Code);
    }

    [Fact]
    public async Task On_sqlite_the_sequence_starts_above_the_keys_the_table_already_holds()
    {
        await using var database = await StartDatabaseAsync();
        database.Context.Add(SequencedProvider.Imported(1500, "Shell"));
        await database.Context.SaveChangesAsync(Token);

        var added = SequencedProvider.Named("MOL");
        database.Context.Add(added);
        await database.Context.SaveChangesAsync(Token);

        Assert.Equal(1501, added.Id);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
