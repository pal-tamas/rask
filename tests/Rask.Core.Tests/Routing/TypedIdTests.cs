using System.Text.Json;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

public readonly partial record struct TruckId(int Value);

public readonly partial record struct TrailerId(long Value);

public readonly partial record struct DriverId(Guid Value);

public readonly partial record struct PlateId(string Value);

public sealed record Shift(TruckId Truck, DriverId Driver, PlateId Plate, TrailerId? Trailer);

// `public readonly partial record struct TruckId(int Value);` is the whole declaration; TypedIdGenerator does the rest.
public partial class TypedIdTests : global::Rask.Core.RaskMarkup
{
    private static readonly Guid Known = Guid.Parse("3E18AD13-D95E-4808-97BD-918B457C006B");

    [Fact]
    public void An_id_prints_as_its_value_alone()
    {
        var truck = new TruckId(42);
        var driver = new DriverId(Known);

        var printed = (truck.ToString(), driver.ToString(), new PlateId("ABC-123").ToString());

        Assert.Equal(("42", "3e18ad13-d95e-4808-97bd-918b457c006b", "ABC-123"), printed);
    }

    [Fact]
    public void An_id_parses_what_it_prints()
    {
        var truck = new TruckId(-7);
        var trailer = new TrailerId(long.MaxValue);

        var parsed = (TruckId.Parse(truck.ToString()), TrailerId.Parse(trailer.ToString()), DriverId.Parse(Known.ToString()));

        Assert.Equal((truck, trailer, new DriverId(Known)), parsed);
    }

    [Fact]
    public void TryParse_refuses_what_spells_no_id()
    {
        var number = TruckId.TryParse("4x", out _);
        var guid = DriverId.TryParse("not-a-guid", out _);
        var none = PlateId.TryParse(null, out _);

        var plate = PlateId.TryParse("XYZ", out var parsed);

        Assert.False(number);
        Assert.False(guid);
        Assert.False(none);
        Assert.True(plate);
        Assert.Equal(new PlateId("XYZ"), parsed);
    }

    [Fact]
    public void A_string_id_refuses_the_empty_string_and_keeps_whitespace()
    {
        var empty = PlateId.TryParse("", out _);
        var blank = PlateId.TryParse(" ", out var spaced);

        var thrown = Assert.Throws<FormatException>(() => PlateId.Parse(""));

        Assert.False(empty);
        Assert.True(blank);
        Assert.Equal(new PlateId(" "), spaced);
        Assert.Contains("empty", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Guid_id_is_new_each_time_and_time_ordered()
    {
        var first = DriverId.New();
        var second = DriverId.New();

        Assert.NotEqual(first, second);
        Assert.Equal(7, first.Value.Version);
    }

    [Fact]
    public void Ten_thousand_Guid_ids_made_in_a_tight_loop_strictly_increase()
    {
        var ids = new DriverId[10_000];

        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = DriverId.New();
        }

        Assert.All(ids.Zip(ids.Skip(1)), pair => Assert.True(pair.First.Value.CompareTo(pair.Second.Value) < 0));
        Assert.All(ids, id => Assert.Equal((7, 0b10), (id.Value.Version, id.Value.Variant >> 2)));
    }

    [Fact]
    public void Guid_ids_made_on_many_threads_at_once_are_all_distinct_and_each_threads_increase()
    {
        const int perThread = 2_000;
        var made = new DriverId[Environment.ProcessorCount][];

        Parallel.For(0, made.Length, t =>
        {
            made[t] = new DriverId[perThread];
            for (var i = 0; i < perThread; i++)
            {
                made[t][i] = DriverId.New();
            }
        });

        Assert.Equal(made.Length * perThread, made.SelectMany(static ids => ids).Distinct().Count());
        Assert.All(made, ids => Assert.All(ids.Zip(ids.Skip(1)), pair => Assert.True(pair.First.Value.CompareTo(pair.Second.Value) < 0)));
    }

    [Fact]
    public void A_Guid_id_carries_the_current_Unix_millisecond_in_its_first_six_bytes()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var id = DriverId.New();

        Span<byte> bytes = stackalloc byte[16];
        id.Value.TryWriteBytes(bytes, bigEndian: true, out _);
        var stamp = (long)(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes) >> 16);
        // A busy millisecond (4096 ids) borrows the next one, so a test that just made thousands may run a little ahead.
        Assert.InRange(stamp, before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 100);
    }

    [Fact]
    public void An_id_travels_as_its_value_in_JSON()
    {
        var shift = new Shift(new TruckId(42), new DriverId(Known), new PlateId("ABC-123"), new TrailerId(9));

        var json = JsonSerializer.Serialize(shift);

        Assert.Equal(
            """{"Truck":42,"Driver":"3e18ad13-d95e-4808-97bd-918b457c006b","Plate":"ABC-123","Trailer":9}""", json);
        Assert.Equal(shift, JsonSerializer.Deserialize<Shift>(json));
    }

    [Fact]
    public void A_number_id_written_as_text_is_still_read()
    {
        var json = """{"Truck":"42","Driver":"3e18ad13-d95e-4808-97bd-918b457c006b","Plate":"P","Trailer":null}""";

        var shift = JsonSerializer.Deserialize<Shift>(json)!;

        Assert.Equal(new TruckId(42), shift.Truck);
        Assert.Null(shift.Trailer);
    }

    [Fact]
    public void An_id_keys_a_JSON_dictionary_by_its_value()
    {
        var mileage = new Dictionary<TruckId, int> { [new TruckId(1)] = 100, [new TruckId(2)] = 250 };

        var json = JsonSerializer.Serialize(mileage);

        Assert.Equal("""{"1":100,"2":250}""", json);
        Assert.Equal(mileage, JsonSerializer.Deserialize<Dictionary<TruckId, int>>(json));
    }

    [Fact]
    public void A_route_segment_binds_to_a_typed_id_property()
    {
        var page = new TruckPage();

        PageBinder.Bind(page, new Dictionary<string, string?> { ["id"] = "42" }, new QueryCollection());

        Assert.Equal(new TruckId(42), page.Id);
    }

    [Fact]
    public void A_generated_Url_writes_a_typed_id_as_its_value()
    {
        var driver = new DriverId(Known);

        var segment = RouteValueFormatter.Format(driver);

        Assert.Equal("3e18ad13-d95e-4808-97bd-918b457c006b", segment);
    }

    [Fact]
    public void A_route_segment_that_spells_no_id_is_refused()
    {
        var page = new TruckPage();

        Assert.Throws<RouteBindException>(() =>
            PageBinder.Bind(page, new Dictionary<string, string?> { ["id"] = "nope" }, new QueryCollection()));
    }

    [SkipFactory]
    private sealed class TruckPage : Component
    {
        [RouteParam] public TruckId Id { get; set; }

        protected override Component? Render() => Span;
    }
}
