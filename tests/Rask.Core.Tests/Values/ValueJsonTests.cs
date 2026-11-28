using System.Text.Json;
using Rask.Cqrs;
using Rask.Values;
using Text = Rask.Values.Text;

namespace Rask.Core.Tests.Values;

public sealed partial record JsonDriver(string Value) : Text(Value, MaxLength: 100);

public sealed partial record JsonContact(string Value) : Email(Value);

public sealed partial record JsonMobile(string Value) : Phone(Value);

public sealed partial record JsonSite(string Value) : Url(Value);

public sealed partial record JsonNote(string Value) : Multiline(Value);

public sealed partial record JsonLitres(decimal Value) : Number(Value, Min: 1, Scale: 2);

public sealed partial record JsonPieces(long Value) : Integer(Value, Min: 0);

public sealed partial record JsonSeats(int Value) : Integer(Value, Min: 1);

public sealed partial record JsonDueOn(DateOnly Value) : Day(Value);

public sealed partial record JsonRank(int Position) : Value;

public enum JsonLevel
{
    Low,
    High,
}

public sealed partial record JsonTier(JsonLevel Value) : Value;

public sealed partial record JsonPassword(string Value) : Password(Value, MinLength: 8);

public sealed record JsonDelivery(JsonDriver Driver, JsonDriver? Mate, JsonLitres Fuel, JsonDueOn Due);

// System.Text.Json — a read row an endpoint returns, a job's stored command, an API body — writes a value object of one
// value as the value it holds, as Rask's own wires carry it; the object it was written as before is still read.
public sealed class ValueJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(typeof(JsonDriver), "\"Ada\"", """{"value":"Ada"}""")]
    [InlineData(typeof(JsonContact), "\"ada@example.com\"", """{"Value":"ada@example.com"}""")]
    [InlineData(typeof(JsonMobile), "\"06 30 123 4567\"", """{"VALUE":"06 30 123 4567"}""")]
    [InlineData(typeof(JsonSite), "\"https://rask.sh/\"", """{"value":"https://rask.sh/","kind":"Url"}""")]
    [InlineData(typeof(JsonNote), "\"two\\nlines\"", """{"value":"two\nlines"}""")]
    [InlineData(typeof(JsonLitres), "12.5", """{"value":12.5}""")]
    [InlineData(typeof(JsonPieces), "9000000000", """{"value":9000000000}""")]
    [InlineData(typeof(JsonSeats), "4", """{"value":4}""")]
    [InlineData(typeof(JsonDueOn), "\"2026-11-28\"", """{"value":"2026-11-28"}""")]
    [InlineData(typeof(JsonRank), "3", """{"position":3}""")]
    [InlineData(typeof(JsonTier), "1", """{"value":1}""")]
    public void A_value_object_of_one_value_is_written_as_that_value_and_read_from_it_or_its_older_object(Type type, string bare, string older)
    {
        var fromBare = JsonSerializer.Deserialize(bare, type, Web);

        var written = JsonSerializer.Serialize(fromBare, type, Web);
        var fromOlder = JsonSerializer.Deserialize(older, type, Web);

        Assert.Equal(bare, written);
        Assert.Equal(bare, JsonSerializer.Serialize(fromOlder, type, Web));
    }

    [Fact]
    public void A_read_row_of_value_objects_is_written_as_their_values_a_missing_one_as_null()
    {
        var delivery = new JsonDelivery(JsonDriver.Create("Ada"), null, JsonLitres.Create(12.5m), JsonDueOn.Create(new DateOnly(2026, 11, 28)));

        var json = JsonSerializer.Serialize(delivery, Web);
        var back = JsonSerializer.Deserialize<JsonDelivery>(json, Web)!;

        Assert.Equal("""{"driver":"Ada","mate":null,"fuel":12.5,"due":"2026-11-28"}""", json);
        Assert.Equal("Ada", back.Driver.Value);
        Assert.Null(back.Mate);
        Assert.Equal(12.5m, back.Fuel.Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("""{"value":0}""")]
    public void A_value_its_rules_refuse_is_refused_when_read_in_either_form(string json)
    {
        var read = () => JsonSerializer.Deserialize<JsonSeats>(json, Web);

        var refused = Record.Exception(read);

        Assert.IsType<RaskValidationException>(refused);
    }

    [Fact]
    public void An_older_object_without_the_member_is_refused_as_json_that_holds_no_value()
    {
        var read = () => JsonSerializer.Deserialize<JsonRank>("""{"value":3}""", Web);

        var refused = Record.Exception(read);

        Assert.IsType<JsonException>(refused);
    }

    [Fact]
    public void A_password_is_given_no_bare_form()
    {
        var password = JsonPassword.Create("correct horse");

        var json = JsonSerializer.Serialize(password, Web);

        Assert.StartsWith("{", json, StringComparison.Ordinal);
    }
}
