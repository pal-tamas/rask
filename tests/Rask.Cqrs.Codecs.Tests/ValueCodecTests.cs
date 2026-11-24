using static Rask.Cqrs.Codecs.Tests.CodecWire;

namespace Rask.Cqrs.Codecs.Tests;

// The codec both hosts run — the server reading what the browser sent, the browser reading what the server answered —
// over the value objects, typed ids and record structs a domain holds.
public sealed class ValueCodecTests
{
    [Fact]
    public void A_message_of_value_objects_typed_ids_and_record_structs_survives_the_round_trip()
    {
        var original = Assignment();

        var round = RoundTrip(original);

        Assert.Equal(original.Vehicle, round.Vehicle);
        Assert.Equal(original.Spare, round.Spare);
        Assert.Equal(original.Trip, round.Trip);
        Assert.Equal(original.Driver, round.Driver);
        Assert.Null(round.CoDriver);
        Assert.Equal(original.Fuel, round.Fuel);
        Assert.Equal(original.Km, round.Km);
        Assert.Equal(original.Reading, round.Reading);
        Assert.Equal(original.Fleet, round.Fleet);
    }

    [Fact]
    public void A_one_value_value_object_and_a_typed_id_travel_as_the_bare_value()
    {
        var json = Write(Assignment());

        Assert.Contains("\"vehicle\":42", json, StringComparison.Ordinal);
        Assert.Contains("\"spare\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"driver\":\"ABC-123\"", json, StringComparison.Ordinal);
        Assert.Contains("\"fuel\":12.5", json, StringComparison.Ordinal);
        Assert.Contains("\"fleet\":[1,2]", json, StringComparison.Ordinal);
        Assert.Contains("\"km\":{\"start\":10,\"end\":20}", json, StringComparison.Ordinal);
        Assert.Contains("\"reading\":{\"km\":1200,\"metres\":40}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_its_rules_would_refuse_is_read_as_sent_rather_than_thrown()
    {
        const string json = """{"vehicle":1,"trip":"6f9619ff-8b86-d011-b42d-00c04fc964ff","driver":"far too long a name","fuel":0,"km":{"start":20,"end":10},"reading":{"km":0,"metres":0},"fleet":[]}""";

        var read = (AssignDriver)Read(Contract<AssignDriver>(), json, []);

        Assert.Equal("far too long a name", read.Driver.Value);
        Assert.Equal(0m, read.Fuel.Value);
        Assert.Equal(20, read.Km.Start);
        Assert.Equal(10, read.Km.End);
    }

    [Fact]
    public void A_value_made_after_a_read_checks_its_rules_again()
    {
        const string json = """{"vehicle":1,"trip":"6f9619ff-8b86-d011-b42d-00c04fc964ff","driver":"far too long a name","fuel":1,"km":{"start":1,"end":2},"reading":{"km":0,"metres":0},"fleet":[]}""";
        Read(Contract<AssignDriver>(), json, []);

        var refused = Record.Exception(() => DriverName.Create("far too long a name"));

        Assert.IsType<RaskValidationException>(refused);
    }

    [Fact]
    public void A_typed_id_result_travels_as_its_value()
    {
        var json = WriteResult<AssignDriver>(new VehicleId(7));

        var read = ReadResult<AssignDriver>(json);

        Assert.Equal("7", json);
        Assert.Equal(new VehicleId(7), read);
    }

    private static AssignDriver Assignment() => new(
        Vehicle: new VehicleId(42),
        Spare: null,
        Trip: new TripKey(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")),
        Driver: DriverName.Create("ABC-123"),
        CoDriver: null,
        Fuel: Litres.Create(12.5m),
        Km: KmRange.Create(10, 20),
        Reading: new Odometer(1200, 40),
        Fleet: [new VehicleId(1), new VehicleId(2)]);
}
