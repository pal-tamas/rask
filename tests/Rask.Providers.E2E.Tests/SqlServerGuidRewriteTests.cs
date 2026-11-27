using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     The one-time rewrite on a real SQL Server: a database written in SQL Server's native layout, rewritten by the
///     migration Rask generates, reads back the same ids — in time order — through the model that stores them by time.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerGuidRewriteTests : IAsyncLifetime
{
    private const string Database = "rask_e2e_guid_rewrite";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await SqlServer.DropDatabaseAsync(Database);
        }
    }

    [Fact]
    public async Task Rows_written_natively_read_back_the_same_ids_in_time_order_after_the_rewrite()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var ids = Enumerable.Range(0, 300).Select(static _ => ConvoyId.New()).ToList();
        await using (var native = Native())
        {
            await native.Database.EnsureDeletedAsync(Ct);
            await native.Database.EnsureCreatedAsync(Ct);
            native.Set<Convoy>().AddRange(ids.AsEnumerable().Reverse().Select(static (id, i) => Convoy.Named(id, "c" + i, id.Value, null)));
            await native.SaveChangesAsync(Ct);
            native.Set<ConvoyLeg>().AddRange(ids.Select(static id => ConvoyLeg.Of(id, 1)));
            await native.SaveChangesAsync(Ct);
        }

        await using var ordered = Ordered();
        await using (var before = Native())
        {
            var rewrite = ordered.GetService<IMigrationsModelDiffer>().GetDifferences(
                before.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                ordered.GetService<IDesignTimeModel>().Model.GetRelationalModel());
            foreach (var command in ordered.GetService<IMigrationsSqlGenerator>().Generate(rewrite, ordered.Model))
            {
                await command.ExecuteNonQueryAsync(ordered.GetService<IRelationalConnection>(), null, Ct);
            }
        }

        var read = await ordered.Set<Convoy>().AsNoTracking().OrderBy(c => c.Id).ToListAsync(Ct);
        var joined = await ordered.Set<ConvoyLeg>().Join(ordered.Set<Convoy>(), l => l.Convoy, c => c.Id, (l, c) => c.Id).CountAsync(Ct);

        Assert.Equal(ids.Order().ToList(), read.Select(static c => c.Id).ToList());
        Assert.All(read, static c => Assert.Equal(c.Id.Value, c.Ticket));
        Assert.Equal(ids.Count, joined);
    }

    [Fact]
    public async Task The_stored_form_of_an_id_s_text_in_T_SQL_is_the_one_Rask_writes()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var id = ConvoyId.New().Value;
        await using var connection = new SqlConnection(SqlServer.Database("master"));
        await connection.OpenAsync(Ct);

        await using var command = new SqlCommand(
            "DECLARE @id nvarchar(36) = @text; SELECT " + SqlServerGuidOrder.StoredFromText("@id") + ", "
            + SqlServerGuidOrder.StoredFromNative("@native") + ", " + SqlServerGuidOrder.NativeFromStored("@stored"),
            connection);
        command.Parameters.AddWithValue("@text", id.ToString());
        command.Parameters.AddWithValue("@native", id);
        command.Parameters.AddWithValue("@stored", SqlServerGuidOrder.ToStore(id));
        await using var reader = await command.ExecuteReaderAsync(Ct);
        await reader.ReadAsync(Ct);

        Assert.Equal(SqlServerGuidOrder.ToStore(id), reader.GetGuid(0));
        Assert.Equal(SqlServerGuidOrder.ToStore(id), reader.GetGuid(1));
        Assert.Equal(id, reader.GetGuid(2));
    }

    private static ConvoyDbContext Ordered() =>
        new(new DbContextOptionsBuilder<ConvoyDbContext>().UseRaskSqlServerAt(SqlServer.Database(Database)).Options);

    private static NativeConvoyDbContext Native() =>
        new(new DbContextOptionsBuilder<NativeConvoyDbContext>().UseRaskSqlServerAt(SqlServer.Database(Database)).Options);
}
