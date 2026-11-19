using System.Globalization;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-helper subclasses have no generated factory
#pragma warning disable RASK025 // the point: a number bound to a TEXT field, which the analyzer warns about

namespace Rask.Core.Tests.Forms;

// A number in a TEXT field was shown in the parsers' spelling ("1234.5") and read by guessing, so a German reader's
// "1.234" was 1.234. It is shown and read in the reader's culture now, and comes back as it went out; a number
// input (type=number) is untouched — the browser sends it in the parsers' spelling whatever the language.
public partial class TextNumberCultureTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("de-DE", "1234,5")]
    [InlineData("hu-HU", "1234,5")]
    [InlineData("en-US", "1234.5")]
    public void A_number_in_a_text_field_is_shown_with_the_readers_decimal_mark(string culture, string shown)
    {
        var m = new Tank { Price = 1234.5m };
        var html = Under(culture, () => Page.Render(() => Form.Model(m)[Input.Bind(() => m.Price).Type(InputType.Text)]).Html);

        Assert.Contains($"value=\"{shown}\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de-DE", "1.234", 1234)]
    [InlineData("de-DE", "1.234,5", 1234.5)]
    [InlineData("de-DE", "1234,5", 1234.5)]
    [InlineData("hu-HU", "1 234,5", 1234.5)]
    [InlineData("hu-HU", "12,5", 12.5)]
    [InlineData("en-US", "1,234.5", 1234.5)]
    public async Task A_number_typed_into_a_text_field_is_read_in_the_readers_culture(string culture, string typed, double price)
    {
        var m = new Tank();
        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Price).Type(InputType.Text).InputMode(InputMode.Decimal)]);

        await UnderAsync(culture, () => page.On("input").Change(typed));

        Assert.Equal((decimal)price, m.Price);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("hu-HU")]
    public async Task What_a_text_field_shows_reads_back_as_the_same_number(string culture)
    {
        var m = new Tank { Price = 1.234m };
        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Price).Type(InputType.Text)]);
        var shown = Under(culture, () => ReaderNumbersShow(m.Price));

        await UnderAsync(culture, () => page.On("input").Change(shown));

        Assert.Equal("1,234", shown);
        Assert.Equal(1.234m, m.Price);
    }

    [Fact]
    public async Task A_number_input_still_reads_the_browsers_spelling_under_any_culture()
    {
        var m = new Tank();
        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Price)]);
        var html = Under("de-DE", () => Page.Render(() => Form.Model(new Tank { Price = 1.5m })[Input.Bind(() => m.Price)]).Html);

        await UnderAsync("de-DE", () => page.On("input").Change("1.234"));

        Assert.Equal(1.234m, m.Price);
        Assert.Contains("type=\"number\"", html, StringComparison.Ordinal);
    }

    private static string ReaderNumbersShow(object value) => ReaderNumbers.Instance.Show(value);

    private static T Under<T>(string culture, Func<T> act)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private static async Task UnderAsync(string culture, Func<Task> act)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            await act();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private sealed class Tank
    {
        public decimal Price { get; set; }
    }
}
