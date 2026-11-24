using static Rask.Cqrs.Codecs.Tests.CodecWire;

namespace Rask.Cqrs.Codecs.Tests;

// The codec both hosts run — the server reading what the browser sent, checked; the browser reading what the server
// answered, loaded as sent — over the value objects, typed ids and record structs a domain holds.
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
    public void A_message_holding_values_their_rules_refuse_is_refused_with_every_field_named()
    {
        const string json = """{"vehicle":1,"trip":"6f9619ff-8b86-d011-b42d-00c04fc964ff","driver":"far too long a name","fuel":0,"km":{"start":20,"end":10},"reading":{"km":0,"metres":0},"fleet":[]}""";

        var refused = Assert.Throws<RaskValidationException>(() => Read(Contract<AssignDriver>(), json, []));

        Assert.Contains("Driver", refused.Errors.Keys);
        Assert.Contains("Fuel", refused.Errors.Keys);
        Assert.Contains(refused.Errors.Keys, key => key.StartsWith("Km", StringComparison.Ordinal));
        Assert.Equal(3, refused.Failures.Count);
    }

    [Fact]
    public void A_refusal_inside_a_list_or_a_map_is_named_by_where_it_sits()
    {
        const string json = """{"seats":[{"driver":"ABC"},{"driver":"far too long a name"}],"reserves":{"night":"also far too long"}}""";

        var refused = Assert.Throws<RaskValidationException>(() => Read(Contract<SeatCrew>(), json, []));

        Assert.Equal(["Reserves[night]", "Seats[1].Driver"], refused.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_message_whose_values_keep_their_rules_is_read_with_them_made_as_create_makes_them()
    {
        const string json = """{"vehicle":1,"trip":"6f9619ff-8b86-d011-b42d-00c04fc964ff","driver":"  ABC  ","fuel":2,"km":{"start":1,"end":2},"reading":{"km":0,"metres":0},"fleet":[]}""";

        var read = (AssignDriver)Read(Contract<AssignDriver>(), json, []);

        Assert.Equal(DriverName.Create("ABC"), read.Driver);
        Assert.Equal("ABC", read.Driver.Value);
        Assert.Equal(KmRange.Create(1, 2), read.Km);
    }

    [Fact]
    public void A_message_leaving_out_a_member_declared_without_a_question_mark_is_refused_as_required()
    {
        const string json = """{"vehicle":1,"trip":"6f9619ff-8b86-d011-b42d-00c04fc964ff","fuel":2,"km":{"start":1,"end":2},"reading":{"km":0,"metres":0}}""";

        var refused = Assert.Throws<RaskValidationException>(() => Read(Contract<AssignDriver>(), json, []));

        Assert.Equal(["Driver", "Fleet"], refused.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Rask.Rules.Required, refused.Errors["Driver"][0]);
    }

    [Fact]
    public void A_message_sending_null_for_a_required_member_is_refused_while_an_optional_one_stays_null()
    {
        const string json = """{"vehicle":1,"trip":"6f9619ff-8b86-d011-b42d-00c04fc964ff","driver":null,"coDriver":null,"fuel":2,"km":{"start":1,"end":2},"reading":{"km":0,"metres":0},"fleet":[]}""";

        var refused = Assert.Throws<RaskValidationException>(() => Read(Contract<AssignDriver>(), json, []));

        Assert.Equal(["Driver"], refused.Errors.Keys);
    }

    [Fact]
    public void A_result_leaving_out_a_required_member_is_read_as_the_server_sent_it()
    {
        var read = ReadResult<GetCrew>("""{"seats":[]}""");

        Assert.Null(Assert.IsType<SeatCrew>(read).Reserves);
    }

    [Fact]
    public void A_result_holding_a_value_its_rules_would_refuse_is_read_as_the_server_sent_it()
    {
        var read = ReadResult<GetDriver>("\"far too long a name\"");

        Assert.Equal("far too long a name", Assert.IsType<DriverName>(read).Value);
    }

    [Fact]
    public void An_event_the_server_pushes_is_loaded_as_sent_while_one_a_client_sends_is_checked()
    {
        const string json = """{"name":"far too long a name"}""";
        var contract = Contract<DriverRenamed>();

        var loaded = Load(contract, json);
        var refused = Record.Exception(() => Read(contract, json, []));

        Assert.Equal("far too long a name", Assert.IsType<DriverRenamed>(loaded).Name.Value);
        Assert.IsType<RaskValidationException>(refused);
    }

    [Fact]
    public void A_value_made_after_a_loaded_result_checks_its_rules_again()
    {
        ReadResult<GetDriver>("\"far too long a name\"");

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
