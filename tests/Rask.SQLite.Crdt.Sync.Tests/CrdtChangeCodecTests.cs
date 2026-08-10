using System.Text;

namespace Rask.SQLite.Crdt.Sync.Tests;

/// <summary>
///     A value written back as the wrong storage class is not a formatting difference — it is a
///     different value, and it lands in a peer's database silently. So the round trip is asserted per
///     type rather than in aggregate.
/// </summary>
public sealed class CrdtChangeCodecTests
{
    public static TheoryData<object?> Values => new()
    {
        null,
        42L,
        -1L,
        long.MaxValue,
        3.5d,
        "hello",
        string.Empty,
        "unicode ☕ and \"quotes\"",
        new byte[] { 1, 2, 3, 0xFF },
        Array.Empty<byte>(),
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void A_value_round_trips_with_its_type(object? value)
    {
        var decoded = RoundTrip(Change(value));

        if (value is byte[] blob)
        {
            Assert.Equal(blob, Assert.IsType<byte[]>(decoded.Value));
        }
        else
        {
            Assert.Equal(value, decoded.Value);
            Assert.Equal(value?.GetType(), decoded.Value?.GetType());
        }
    }

    [Fact]
    public void A_number_stays_a_number_and_a_string_stays_a_string()
    {
        // The failure this guards is subtle: "42" and 42 compare equal to a careless reader but are
        // different SQLite storage classes, and a column that silently changes type breaks queries on
        // the receiving device rather than on the sending one.
        Assert.IsType<long>(RoundTrip(Change(42L)).Value);
        Assert.IsType<string>(RoundTrip(Change("42")).Value);
        Assert.IsType<double>(RoundTrip(Change(42.0d)).Value);
    }

    [Fact]
    public void Every_field_survives()
    {
        var change = new CrdtChange(
            "Todos", [9, 8, 7], "Title", "x",
            ColumnVersion: 3, DbVersion: 11, SiteId: [1, 2], CausalLength: 2, Sequence: 5);

        var decoded = RoundTrip(change);

        Assert.Equal(change.Table, decoded.Table);
        Assert.Equal(change.PrimaryKey, decoded.PrimaryKey);
        Assert.Equal(change.ColumnName, decoded.ColumnName);
        Assert.Equal(change.ColumnVersion, decoded.ColumnVersion);
        Assert.Equal(change.DbVersion, decoded.DbVersion);
        Assert.Equal(change.SiteId, decoded.SiteId);
        Assert.Equal(change.CausalLength, decoded.CausalLength);
        Assert.Equal(change.Sequence, decoded.Sequence);
    }

    [Fact]
    public void Order_is_preserved()
    {
        // Changes are applied in the order they were made; a codec that reordered them would break
        // causality for a receiver, and nothing else in the pipeline re-sorts.
        var batch = Enumerable.Range(0, 50)
            .Select(i => Change($"v{i}") with { DbVersion = i })
            .ToList();

        Assert.Equal(
            batch.Select(c => c.DbVersion),
            CrdtChangeCodec.Decode(CrdtChangeCodec.Encode(batch)).Select(c => c.DbVersion));
    }

    [Fact]
    public void An_empty_batch_round_trips()
    {
        Assert.Empty(CrdtChangeCodec.Decode(CrdtChangeCodec.Encode([])));
    }

    [Fact]
    public void A_newer_format_is_refused_rather_than_misread()
    {
        // An object written today may be read years later by a device that has been offline since. Half
        // a schema applied from a format this build does not understand would be worse than an error.
        var future = Encoding.UTF8.GetBytes("""{"v":99,"changes":[]}""");

        var error = Assert.Throws<InvalidOperationException>(() => CrdtChangeCodec.Decode(future));
        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    private static CrdtChange Change(object? value) =>
        new("Todos", [1], "Title", value, 1, 1, [0xAA], 1, 0);

    private static CrdtChange RoundTrip(CrdtChange change) =>
        CrdtChangeCodec.Decode(CrdtChangeCodec.Encode([change])).Single();
}
