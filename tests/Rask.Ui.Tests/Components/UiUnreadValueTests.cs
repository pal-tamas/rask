using System.Globalization;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Text the kit's input cannot read into the bound member is said under the field, in the reader's language,
///     out of the kit's own catalog: the app writes no rule and ships no translation for it.
/// </summary>
public partial class UiUnreadValueTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(nameof(Tank.Weight), "1.5", "Egész számot adjon meg.")]
    [InlineData(nameof(Tank.Weight), "99999999999", "A szám kívül esik a megengedett tartományon.")]
    [InlineData(nameof(Tank.Price), "12.5.0", "Számot adjon meg.")]
    [InlineData(nameof(Tank.Day), "2026-02-31", "Érvényes dátumot adjon meg.")]
    [InlineData(nameof(Tank.At), "25:99", "Érvényes időpontot adjon meg.")]
    public async Task The_kits_input_says_in_hungarian_what_it_could_not_read_and_keeps_the_stored_value(
        string member, string typed, string hungarian)
    {
        var m = new Tank();
        var before = m.ToString();
        var saved = 0;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            member switch
            {
                nameof(Tank.Weight) => Ui.Input.Bind(() => m.Weight).Label("Weight").Id("field"),
                nameof(Tank.Price) => Ui.Input.Bind(() => m.Price).Label("Price").Id("field"),
                nameof(Tank.Day) => Ui.Input.Bind(() => m.Day).Label("Day").Id("field"),
                _ => (Rask.Core.Component)Ui.Input.Bind(() => m.At).Label("At").Id("field"),
            }
        ]);
        var language = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hu-HU");

        await page.On("#field").Change(typed);
        await page.On("form").Submit();
        CultureInfo.CurrentUICulture = language;

        Assert.Equal(hungarian, page.TextOf("#field-error"));
        Assert.Equal("true", page.Find("#field").Attribute("aria-invalid"));
        Assert.Equal(0, saved);
        Assert.Equal(before, m.ToString());
    }

    [Fact]
    public async Task A_correction_the_member_can_hold_takes_the_sentence_away_and_saves()
    {
        var m = new Tank();
        var saved = 0;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            Ui.Input.Bind(() => m.Weight).Label("Weight").Id("field")]);

        await page.On("#field").Change("1.5");
        var said = page.TextOf("#field-error");
        await page.On("#field").Change("7000");
        await page.On("form").Submit();

        Assert.Equal("Enter a whole number.", said);
        Assert.Equal("", page.TextOf("#field-error"));
        Assert.Equal(7000, m.Weight);
        Assert.Equal(1, saved);
    }

    private sealed class Tank
    {
        public int? Weight { get; set; } = 6500;

        public decimal? Price { get; set; } = 27.5m;

        public DateOnly? Day { get; set; } = new(2026, 10, 12);

        public TimeOnly? At { get; set; } = new(9, 30);

        public override string ToString() => FormattableString.Invariant($"{Weight}|{Price}|{Day:O}|{At:O}");
    }
}
