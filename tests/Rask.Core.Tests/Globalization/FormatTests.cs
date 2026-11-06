using System.Globalization;

namespace Rask.Core.Tests.Globalization;

[Collection(CultureCollection.Name)]
public sealed class FormatTests : IDisposable
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Fact]
    public void A_day_a_moment_and_a_number_are_written_as_Hungarian_writes_them()
    {
        CultureInfo.CurrentCulture = Hungarian;
        var space = Hungarian.NumberFormat.NumberGroupSeparator;

        var day = Format.Day(new DateOnly(2026, 10, 9));
        var moment = Format.Moment(new DateTime(2026, 10, 9, 14, 35, 0));
        var whole = Format.Number(24000);
        var litres = Format.Number(1764.600000000m);
        var amount = Format.Number(1764.6m, 2);

        Assert.Equal("2026. 10. 09.", day);
        Assert.Equal("2026. 10. 09. 14:35", moment);
        Assert.Equal($"24{space}000", whole);
        Assert.Equal($"1{space}764,6", litres);
        Assert.Equal($"1{space}764,60", amount);
    }

    [Fact]
    public void A_Hungarian_moment_before_ten_writes_its_hour_in_two_digits_so_a_column_lines_up()
    {
        CultureInfo.CurrentCulture = Hungarian;

        var morning = Format.Moment(new DateTime(2026, 1, 2, 9, 5, 0));
        var offset = Format.Moment(new DateTimeOffset(2026, 1, 2, 9, 5, 0, TimeSpan.Zero).ToOffset(TimeSpan.Zero));

        Assert.Equal("2026. 01. 02. 09:05", morning);
        Assert.EndsWith(":05", offset, StringComparison.Ordinal);
    }

    [Fact]
    public void An_English_moment_keeps_its_twelve_hour_clock_unpadded()
    {
        CultureInfo.CurrentCulture = English;

        var morning = Format.Moment(new DateTime(2026, 1, 2, 9, 5, 0));

        Assert.StartsWith("1/2/2026 9:05", morning, StringComparison.Ordinal);
        Assert.EndsWith("AM", morning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_day_a_moment_and_a_number_are_written_as_English_writes_them()
    {
        CultureInfo.CurrentCulture = English;

        var day = Format.Day(new DateOnly(2026, 10, 9));
        var moment = Format.Moment(new DateTime(2026, 10, 9, 14, 35, 0));
        var whole = Format.Number(24000);
        var litres = Format.Number(1764.6);
        var amount = Format.Number(1764.6, 2);

        Assert.Equal("10/9/2026", day);
        Assert.StartsWith("10/9/2026 2:35", moment, StringComparison.Ordinal);
        Assert.EndsWith("PM", moment, StringComparison.Ordinal);
        Assert.Equal("24,000", whole);
        Assert.Equal("1,764.6", litres);
        Assert.Equal("1,764.60", amount);
    }

    [Fact]
    public void A_number_to_at_most_two_places_drops_the_padding_zeros_and_rounds_half_away_from_zero()
    {
        CultureInfo.CurrentCulture = Hungarian;
        var space = Hungarian.NumberFormat.NumberGroupSeparator;

        var whole = Format.Number(1200m, atMost: 2);
        var one = Format.Number(1764.604m, atMost: 2);
        var half = Format.Number(0.125m, atMost: 2);
        var real = Format.Number(2.675, atMost: 2);
        var every = Format.Number(1.23456m, atMost: null);

        Assert.Equal($"1{space}200", whole);
        Assert.Equal($"1{space}764,6", one);
        Assert.Equal("0,13", half);
        Assert.Equal("2,68", real);
        Assert.Equal("1,23456", every);
    }

    [Fact]
    public void Hungarian_money_is_written_in_forints_as_Ft_to_at_most_two_places()
    {
        CultureInfo.CurrentCulture = Hungarian;
        var space = Hungarian.NumberFormat.CurrencyGroupSeparator;

        var whole = Format.Money(1200m);
        var half = Format.Money(1200.5m);
        var rounded = Format.Money(1764.604m);
        var huf = Format.Money(1200m, "HUF");
        var owed = Format.Money(-1200.5m);

        Assert.Equal($"1{space}200 Ft", whole);
        Assert.Equal($"1{space}200,5 Ft", half);
        Assert.Equal($"1{space}764,6 Ft", rounded);
        Assert.Equal($"1{space}200 Ft", huf);
        Assert.Equal($"-1{space}200,5 Ft", owed);
        Assert.DoesNotContain("HUF", huf, StringComparison.Ordinal);
    }

    [Fact]
    public void Money_of_another_currency_is_written_in_its_code_where_the_culture_puts_its_sign()
    {
        CultureInfo.CurrentCulture = Hungarian;
        var space = Hungarian.NumberFormat.CurrencyGroupSeparator;

        var hungarian = Format.Money(1764.6m, "eur");
        CultureInfo.CurrentCulture = English;
        var english = Format.Money(1764.6m, "EUR");
        var dollars = Format.Money(1764.6m, "USD");
        var owed = Format.Money(-5m, "EUR");

        Assert.Equal($"1{space}764,6 EUR", hungarian);
        Assert.Equal("EUR 1,764.6", english);
        Assert.Equal("$1,764.6", dollars);
        Assert.Equal("-EUR 5", owed);
    }

    [Fact]
    public void English_money_is_written_in_dollars_to_at_most_two_places()
    {
        CultureInfo.CurrentCulture = English;

        var whole = Format.Money(1200m);
        var cents = Format.Money(19.99m);

        Assert.Equal("$1,200", whole);
        Assert.Equal("$19.99", cents);
    }

    [Theory]
    [InlineData("hu", "Ft")]
    [InlineData("en", "$")]
    public void A_language_alone_writes_its_main_countrys_currency_and_never_the_unknown_sign(string language, string sign)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);

        var money = Format.Money(19.99m);
        var own = Format.Money(19.99m, language == "hu" ? "HUF" : "USD");

        Assert.Contains(sign, money, StringComparison.Ordinal);
        Assert.Equal(money, own);
        Assert.DoesNotContain("¤", money, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("€UR")]
    public void A_currency_that_is_no_ISO_code_is_refused_with_the_call_that_works(string currency)
    {
        CultureInfo.CurrentCulture = English;

        var refused = Assert.Throws<ArgumentException>(() => Format.Money(1m, currency));

        Assert.Contains("Format.Money(amount, \"EUR\")", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_UTC_moment_is_written_on_the_wall_clock_and_an_unspecified_one_as_it_is()
    {
        CultureInfo.CurrentCulture = Hungarian;
        using var zone = AmbientClock.UseZone(Budapest);
        var lateUtc = new DateTimeOffset(2026, 10, 9, 22, 35, 0, TimeSpan.Zero);

        var moment = Format.Moment(lateUtc);
        var day = Format.Day(lateUtc);
        var utcDateTime = Format.Moment(lateUtc.UtcDateTime);
        var wallDateTime = Format.Moment(new DateTime(2026, 10, 9, 22, 35, 0, DateTimeKind.Unspecified));

        Assert.Equal("2026. 10. 10. 00:35", moment);
        Assert.Equal("2026. 10. 10.", day);
        Assert.Equal("2026. 10. 10. 00:35", utcDateTime);
        Assert.Equal("2026. 10. 09. 22:35", wallDateTime);
    }
}
