using Rask.Core.Forms;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-helper subclasses have no generated factory

namespace Rask.Core.Tests.Forms;

// What a bound field reads back is what it showed: a moment in its own offset (not the server's zone), an enum only
// as a member it defines, a duration that is no time of day not cut down to one.
public partial class BoundValueFidelityTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_moment_edited_in_a_datetime_local_field_keeps_its_own_offset()
    {
        var model = new Holder { At = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(5)) };
        var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.At)]);

        Assert.Contains("value=\"2026-11-19T10:00\"", page.Html, StringComparison.Ordinal);
        await page.On("input").Change("2026-11-19T11:30");

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 11, 30, 0, TimeSpan.FromHours(5)), model.At);
    }

    [Fact]
    public async Task A_moment_with_none_before_is_read_as_utc()
    {
        var model = new Holder();
        var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.Since)]);

        await page.On("input").Change("2026-11-19T11:30");

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 11, 30, 0, TimeSpan.Zero), model.Since);
    }

    [Fact]
    public void A_route_value_with_no_offset_is_utc_and_one_with_an_offset_keeps_it()
    {
        Assert.True(RouteValueParser.TryParse(typeof(DateTimeOffset), "2026-11-19T10:00", out var bare));
        Assert.True(RouteValueParser.TryParse(typeof(DateTimeOffset), "2026-11-19T10:00+02:00", out var offset));

        Assert.Equal(TimeSpan.Zero, ((DateTimeOffset)bare!).Offset);
        Assert.Equal(TimeSpan.FromHours(2), ((DateTimeOffset)offset!).Offset);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("Red, 7")]
    public async Task An_enum_field_refuses_a_value_no_member_names(string raw)
    {
        var model = new Holder { Colour = Colour.Green };
        var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.Colour)]);

        await page.On("input").Change(raw);

        Assert.Equal(Colour.Green, model.Colour);
    }

    [Theory]
    [InlineData("blue", Colour.Blue)]
    [InlineData("1", Colour.Green)]
    public async Task An_enum_field_reads_a_member_by_name_or_by_its_defined_number(string raw, Colour expected)
    {
        var model = new Holder();
        var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.Colour)]);

        await page.On("input").Change(raw);

        Assert.Equal(expected, model.Colour);
    }

    [Fact]
    public void A_flags_enum_takes_a_combination_of_its_members_and_refuses_a_bit_it_does_not_have()
    {
        var combined = BindingHelpers.TryParseValue(typeof(Days), "Monday, Friday", out var days);
        var unknown = BindingHelpers.TryParseValue(typeof(Days), "64", out _);

        Assert.True(combined);
        Assert.Equal(Days.Monday | Days.Friday, days);
        Assert.False(unknown);
    }

    [Theory]
    [InlineData(1, 2, 0, "1.02:00:00")]
    [InlineData(0, -1, -30, "-01:30:00")]
    [InlineData(0, 9, 5, "09:05")]
    public void A_duration_that_is_no_time_of_day_is_written_whole(int days, int hours, int minutes, string expected)
    {
        var text = BindingHelpers.FormatValue(new TimeSpan(days, hours, minutes, 0));

        Assert.Equal(expected, text);
    }

    public enum Colour
    {
        Red,
        Green,
        Blue,
    }

    [Flags]
    public enum Days
    {
        None = 0,
        Monday = 1,
        Friday = 16,
    }

    private sealed class Holder
    {
        public DateTimeOffset At { get; set; }

        public DateTimeOffset? Since { get; set; }

        public Colour Colour { get; set; }
    }
}
