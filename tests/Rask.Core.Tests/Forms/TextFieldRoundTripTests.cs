using System.Globalization;
using System.Text.RegularExpressions;

#pragma warning disable RASK014 // test-helper subclasses have no generated factory

namespace Rask.Core.Tests.Forms;

// Any value a field can hold, bound to a TEXT field, comes back as it went out: what the field shows, typed back,
// is the value again. That is what retired RASK025, which warned that a non-string value in a string-only input
// type "could never round-trip" — numbers have since been shown and read in the reader's culture, and the rest are
// shown in the spelling their parsers read.
public partial class TextFieldRoundTripTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("hu-HU")]
    [InlineData("en-US")]
    public async Task Every_kind_of_value_in_a_text_field_reads_back_as_itself(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            await RoundTrips(m => Input.Bind(() => m.Flag).Type(InputType.Text), m => m.Flag, m => m.Flag = true);
            await RoundTrips(m => Input.Bind(() => m.Day).Type(InputType.Text), m => m.Day, m => m.Day = new DateOnly(2026, 11, 19));
            await RoundTrips(m => Input.Bind(() => m.At).Type(InputType.Text), m => m.At, m => m.At = new DateTime(2026, 11, 19, 14, 30, 0));
            await RoundTrips(m => Input.Bind(() => m.Id).Type(InputType.Text), m => m.Id, m => m.Id = Guid.Parse("4f9d6c1e-0b6a-4a51-9c1e-6a0b4f9d6c1e"));
            await RoundTrips(m => Input.Bind(() => m.Colour).Type(InputType.Text), m => m.Colour, m => m.Colour = DayOfWeek.Friday);
            await RoundTrips(m => Input.Bind(() => m.Count).Type(InputType.Text), m => m.Count, m => m.Count = 1234);
            await RoundTrips(m => Input.Bind(() => m.Price).Type(InputType.Text), m => m.Price, m => m.Price = 1234.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // The value set, rendered, and what its field shows typed back into a fresh model: the same value comes out.
    private static async Task RoundTrips<T>(Func<Values, Component> field, Func<Values, T> read, Action<Values> set)
    {
        var shownFrom = new Values();
        set(shownFrom);
        var shown = Shown(Page.Render(() => Form.Model(shownFrom)[field(shownFrom)]).Html);

        var m = new Values();
        var page = Page.Render(() => Form.Model(m)[field(m)]);
        await page.On("input").Change(shown);

        Assert.Equal(read(shownFrom), read(m));
    }

    private static string Shown(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, "value=\"([^\"]*)\"").Groups[1].Value);

    private sealed class Values
    {
        public bool Flag { get; set; }

        public DateOnly Day { get; set; }

        public DateTime At { get; set; }

        public Guid Id { get; set; }

        public DayOfWeek Colour { get; set; }

        public int Count { get; set; }

        public decimal Price { get; set; }
    }
}
