using System.Globalization;
using Rask.Core.Globalization;

namespace Rask.Core.Tests.Globalization;

/// <summary>
///     The US English an English-only browser app formats in without ICU (#1305) writes every value as the runtime's own
///     en-US does: built on the invariant culture's data, as it is in such an app, and compared here, where ICU is.
/// </summary>
public class InvariantEnglishTests
{
    private static readonly CultureInfo Real = CultureInfo.GetCultureInfo("en-US");

    private static readonly CultureInfo Built = InvariantEnglish.Build((CultureInfo)CultureInfo.InvariantCulture.Clone());

    public static TheoryData<string> DateFormats => ["d", "D", "f", "F", "g", "G", "m", "t", "T", "y", "s", "o", "r", "u", "MMM d", "ddd"];

    public static TheoryData<string> NumberFormats => ["C", "C0", "C1", "N", "N0", "N2", "P", "P0", "P1", "F2", "G", "E2", "#,##0.00"];

    [Theory]
    [MemberData(nameof(DateFormats))]
    public void A_moment_is_written_as_us_english_writes_it(string format)
    {
        var moments = new[] { new DateTime(2026, 3, 14, 15, 4, 5), new DateTime(2026, 11, 1, 0, 30, 0), new DateTime(1999, 12, 31, 11, 59, 59) };

        var written = moments.Select(moment => moment.ToString(format, Built));

        Assert.Equal(moments.Select(moment => moment.ToString(format, Real)), written);
    }

    [Theory]
    [MemberData(nameof(NumberFormats))]
    public void A_number_is_written_as_us_english_writes_it(string format)
    {
        var numbers = new[] { 0m, 19.99m, -1234.5m, 1764.604m, 0.125m, -0.5m, 1234567.891m };

        var written = numbers.Select(number => number.ToString(format, Built));

        Assert.Equal(numbers.Select(number => number.ToString(format, Real)), written);
    }

    [Fact]
    public void Infinities_are_written_as_us_english_writes_them()
    {
        var values = new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN };

        var written = values.Select(value => value.ToString(Built));

        Assert.Equal(values.Select(value => value.ToString(Real)), written);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("EN-us", true)]
    [InlineData("en-GB", false)]
    [InlineData("hu", false)]
    [InlineData("", false)]
    public void Only_english_and_us_english_are_written_as_us_english(string name, bool served)
    {
        var serves = InvariantEnglish.Serves(name);

        Assert.Equal(served, serves);
    }
}
