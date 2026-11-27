using System.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

/// <summary>A trip: keyed by a typed id, pointing at a barrel by another, with a plain Guid and an optional one.</summary>
public sealed class OrderedTrip
{
    public BarrelId Id { get; set; }

    public BarrelId Barrel { get; set; }

    public Guid Ticket { get; set; }

    public Guid? Spare { get; set; }

    public Guid TextTicket { get; set; }
}

/// <summary>The trip, with one Guid kept as text by the app's own column type.</summary>
public sealed class OrderedTripContext(DbContextOptions<OrderedTripContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OrderedTrip>().Property(t => t.TextTicket).HasColumnType("nvarchar(36)");
    }
}

public sealed class GuidOrderTests
{
    [Fact]
    public void The_stored_form_reads_back_as_the_same_id()
    {
        var ids = Enumerable.Range(0, 10_000).Select(static i => i % 2 == 0 ? Guid.NewGuid() : Guid.CreateVersion7())
            .Append(Guid.Empty).Append(Guid.AllBitsSet).ToList();

        var back = ids.Select(static id => SqlServerGuidOrder.FromStore(SqlServerGuidOrder.ToStore(id))).ToList();

        Assert.Equal(ids, back);
        Assert.Equal(ids.Count, ids.Select(SqlServerGuidOrder.ToStore).Distinct().Count());
    }

    [Fact]
    public void Stored_ids_sort_on_SQL_Server_as_NET_sorts_the_ids()
    {
        var ids = Enumerable.Range(0, 10_000).Select(static _ => BarrelId.Create().Value)
            .Concat(Enumerable.Range(0, 2_000).Select(static _ => Guid.NewGuid())).ToList();
        var shuffled = ids.OrderBy(static _ => Random.Shared.Next()).ToList();

        var bySqlServer = shuffled.OrderBy(static id => new SqlGuid(SqlServerGuidOrder.ToStore(id))).ToList();
        var natively = shuffled.OrderBy(static id => new SqlGuid(id)).ToList();

        Assert.Equal(ids.Order().ToList(), bySqlServer);
        Assert.NotEqual(ids.Order().ToList(), natively);
    }

    [Fact]
    public void New_typed_ids_stored_are_strictly_increasing_on_SQL_Server()
    {
        var made = Enumerable.Range(0, 10_000).Select(static _ => SqlServerGuidOrder.ToStore(BarrelId.Create().Value)).ToList();

        var ascending = made.Zip(made.Skip(1)).All(static p => new SqlGuid(p.First).CompareTo(new SqlGuid(p.Second)) < 0);

        Assert.True(ascending);
    }

    [Fact]
    public void The_T_SQL_byte_maps_are_the_stored_form_and_back()
    {
        var ids = Enumerable.Range(0, 1_000).Select(static _ => Guid.CreateVersion7()).Append(Guid.NewGuid()).ToList();

        static byte[] Mapped(ReadOnlySpan<byte> map, byte[] from)
        {
            var to = new byte[16];
            for (var j = 0; j < 16; j++)
            {
                to[j] = from[map[j]];
            }

            return to;
        }

        Assert.All(ids, id =>
        {
            var stored = SqlServerGuidOrder.ToStore(id).ToByteArray();
            Assert.Equal(stored, Mapped(SqlServerGuidOrder.StoredFromNativeBytes, id.ToByteArray()));
            Assert.Equal(id.ToByteArray(), Mapped(SqlServerGuidOrder.NativeFromStoredBytes, stored));
            Assert.Equal(stored, Mapped(SqlServerGuidOrder.StoredFromTextBytes, id.ToByteArray(bigEndian: true)));
        });
    }

    [Fact]
    public void The_T_SQL_reads_the_stored_form_from_an_id_s_text_and_rewrites_a_column()
    {
        var fromText = SqlServerGuidOrder.StoredFromText("@id");
        var forward = SqlServerGuidOrder.StoredFromNative("[Id]");
        var back = SqlServerGuidOrder.NativeFromStored("[Id]");

        const string text = "CONVERT(binary(16), REPLACE(@id, '-', ''), 2)";
        Assert.Equal(
            $"CAST(SUBSTRING({text}, 13, 4) + SUBSTRING({text}, 11, 2) + SUBSTRING({text}, 9, 2) + SUBSTRING({text}, 7, 2) + SUBSTRING({text}, 1, 6) AS uniqueidentifier)",
            fromText);
        const string column = "CAST([Id] AS binary(16))";
        Assert.Equal(
            $"CAST(SUBSTRING({column}, 13, 4) + SUBSTRING({column}, 11, 2) + SUBSTRING({column}, 9, 2) + SUBSTRING({column}, 8, 1) + SUBSTRING({column}, 7, 1) + SUBSTRING({column}, 4, 1) + SUBSTRING({column}, 3, 1) + SUBSTRING({column}, 2, 1) + SUBSTRING({column}, 1, 1) + SUBSTRING({column}, 6, 1) + SUBSTRING({column}, 5, 1) AS uniqueidentifier)",
            forward);
        Assert.StartsWith($"CAST(SUBSTRING({column}, 14, 1) + SUBSTRING({column}, 13, 1)", back, StringComparison.Ordinal);
    }

    [Fact]
    public void On_SQLite_the_ids_are_stored_as_they_were()
    {
        using var context = new OrderedTripContext(OptionsLayout.LikeAddDbContext<OrderedTripContext>().UseSqlite("Data Source=:memory:").Options);

        var ticket = context.Model.FindEntityType(typeof(OrderedTrip))!.FindProperty(nameof(OrderedTrip.Ticket))!;

        Assert.Null(context.GetService<IDesignTimeModel>().Model.FindAnnotation(SqlServerGuidOrder.Annotation));
        Assert.False(StoredGuidConverter.IsIn(ticket.GetTypeMapping().Converter));
    }
}
