using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// Every in-memory database over a model after the first is copied from that model's empty schema, not created again.
public sealed class SchemaTemplateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_database_copied_from_the_model_s_schema_has_every_table_and_none_of_another_test_s_rows()
    {
        await using var earlier = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        earlier.Context.Add(Company.Named("Acme"));
        await earlier.Context.SaveChangesAsync(Ct);

        await using var later = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        Assert.Equal(await TablesAsync(earlier), await TablesAsync(later));
        Assert.Equal(0, await later.Context.Set<Company>().CountAsync(Ct));
    }

    [Fact]
    public async Task A_copied_database_takes_writes_as_a_created_one_does()
    {
        await using (await TestDatabase.Start<PumpsContext>(cancellationToken: Ct))
        {
        }

        await using var copied = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        copied.Context.Add(Invoice.For("Csaba", 1m));
        await copied.Context.SaveChangesAsync(Ct);

        Assert.Equal(1, await Invoice.Count(Ct));
    }

    private static async Task<List<string>> TablesAsync(TestDatabase database)
    {
        await using var connection = new SqliteConnection(database.Context.Database.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table', 'index', 'trigger') AND name NOT LIKE 'sqlite_stat%' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var names = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
