using System.Text.Json;
using Rask.Values;

namespace Rask.Core.Tests.Values;

public sealed partial record ShiftStart(DateTime Value) : Moment(Value, NotAfterNow: true);

public sealed record ShiftLogged(string Driver, ShiftStart Start, ShiftStart? End);

// System.Text.Json — a job's stored command, an outbox message, persisted state, an API body — writes a Moment as the
// UTC instant it holds, so the hour Europe/Budapest shows twice in autumn (02:30 at 00:30Z, then at 01:30Z) comes back
// as the instant it was; the forms written before (a bare wall-clock time, the old object) are still read.
public sealed class MomentJsonTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Both_occurrences_of_the_autumn_double_hour_round_trip_through_system_text_json()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        var logged = new ShiftLogged("Anna", ShiftStart.Create(Utc(0, 30)), ShiftStart.Create(Utc(1, 30)));

        var json = JsonSerializer.Serialize(logged, Web);
        var back = JsonSerializer.Deserialize<ShiftLogged>(json, Web)!;

        Assert.Equal("""{"driver":"Anna","start":"2026-10-25T00:30:00Z","end":"2026-10-25T01:30:00Z"}""", json);
        Assert.Equal(logged.Start.Instant, back.Start.Instant);
        Assert.Equal(logged.End!.Instant, back.End!.Instant);
    }

    [Theory]
    [InlineData("\"2026-10-25T02:30:00\"", 0)] // a bare wall-clock time: its first occurrence
    [InlineData("\"2026-10-25T02:30:00+01:00\"", 1)] // an offset names the instant exactly
    [InlineData("""{"value":"2026-10-25T02:30:00","instant":"2026-10-25T01:30:00+00:00","day":"2026-10-25"}""", 1)]
    [InlineData("""{"Value":"2026-10-25T02:30:00","Day":"2026-10-25"}""", 0)] // the old object without its instant
    public void A_moment_written_in_an_older_form_is_still_read_as_the_instant_it_names(string start, int hour)
    {
        using var zone = AmbientClock.UseZone(Budapest);
        var json = """{"driver":"Anna","start":""" + start + ""","end":null}""";

        var back = JsonSerializer.Deserialize<ShiftLogged>(json, Web)!;

        Assert.Equal(new DateTimeOffset(Utc(hour, 30)), back.Start.Instant);
        Assert.Null(back.End);
    }

    [Fact]
    public void A_moment_read_from_json_is_loaded_as_it_was_written_its_rules_not_asked()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        var future = Clock.Now.UtcDateTime.AddYears(1).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        var back = JsonSerializer.Deserialize<ShiftStart>("\"" + future + "Z\"", Web)!;

        Assert.True(back.Instant > Clock.Now);
    }

    private static DateTime Utc(int hour, int minute) => new(2026, 10, 25, hour, minute, 0, DateTimeKind.Utc);
}
