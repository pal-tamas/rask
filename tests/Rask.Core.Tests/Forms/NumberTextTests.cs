using System.Globalization;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A number that arrives as the reader spelled it: typed into a text field over the member, written by a test. The parsers read a comma as a separator
// of thousands wherever it stands, so "12,5" used to be one hundred and twenty-five: it is read in the reader's
// culture or refused, and what a number input sends — digits and a dot — is read the same in every culture.
public partial class NumberTextTests : global::Rask.Core.RaskMarkup
{
    private static NumberFormatInfo Of(string culture) => CultureInfo.GetCultureInfo(culture).NumberFormat;

    [Theory]
    [InlineData("hu-HU", "12,5", "12.5")]
    [InlineData("hu-HU", "1 234,5", "1234.5")]
    [InlineData("hu-HU", "1 234,5", "1234.5")]
    [InlineData("hu-HU", "-1 234 567", "-1234567")]
    [InlineData("hu-HU", "1.234,5", null)]
    [InlineData("hu-HU", "1,234.5", null)]
    [InlineData("hu-HU", "12,", null)]
    [InlineData("hu-HU", "12,5,0", null)]
    [InlineData("en-US", "1,234.5", "1234.5")]
    [InlineData("en-US", "1,234,567", "1234567")]
    [InlineData("en-US", "12,5", null)]
    [InlineData("en-US", "1 234,5", null)]
    [InlineData("en-US", "1.234,5", null)]
    [InlineData("en-US", "1,23,456", null)]
    [InlineData("de-DE", "1.234,5", "1234.5")]
    [InlineData("en-US", "twelve", null)]
    public void Text_the_reader_spelled_is_read_in_their_culture_or_is_no_number(string culture, string typed, string? read)
    {
        var format = Of(culture);

        var spelled = NumberText.Invariant(typed, format);

        Assert.Equal(read, spelled);
    }

    [Theory]
    [InlineData("12.5")]
    [InlineData("-0.5")]
    [InlineData("1e3")]
    [InlineData("99999999999")]
    public void What_a_number_input_sends_is_left_as_it_is(string sent)
    {
        var spelled = NumberText.Invariant(sent);

        Assert.Same(sent, spelled);
    }

    [Theory]
    [InlineData("hu-HU", "12,5", 12.5, "")]
    [InlineData("hu-HU", "1 234,5", 1234.5, "")]
    [InlineData("hu-HU", "12.5", 12.5, "")]
    [InlineData("hu-HU", "1.234,5", 27.5, "Enter a number.")]
    [InlineData("hu-HU", "1,234.5", 27.5, "Enter a number.")]
    [InlineData("en-US", "12,5", 27.5, "Enter a number.")]
    [InlineData("en-US", "1 234,5", 27.5, "Enter a number.")]
    [InlineData("en-US", "1.234,5", 27.5, "Enter a number.")]
    [InlineData("en-US", "1,234.5", 1234.5, "")]
    [InlineData("en-US", "12.5", 12.5, "")]
    public async Task A_bound_decimal_reads_a_comma_in_the_readers_culture_and_never_as_thousands_out_of_place(
        string culture, string typed, double price, string said)
    {
        var m = new Tank();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Price), Test.EditContextProbe(c => ctx = c)]);
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        await page.On("input").Change(typed);
        CultureInfo.CurrentCulture = before;

        Assert.Equal((decimal)price, m.Price);
        Assert.Equal(said, string.Join('|', ctx!.GetValidationMessages()));
    }

    [Fact]
    public async Task A_bound_whole_number_reads_groups_and_refuses_a_fraction_spelled_with_a_comma()
    {
        var m = new Tank();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Weight), Test.EditContextProbe(c => ctx = c)]);
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");

        await page.On("input").Change("6 500");
        var grouped = m.Weight;
        await page.On("input").Change("12,5");
        CultureInfo.CurrentCulture = before;

        Assert.Equal(6500, grouped);
        Assert.Equal(6500, m.Weight);
        Assert.Equal(["Enter a whole number."], ctx!.GetValidationMessages());
    }

    private sealed class Tank
    {
        public int Weight { get; set; }

        public decimal Price { get; set; } = 27.5m;
    }
}
