using System.Globalization;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>mask:dynamic="$money($input)"</c>: an amount shown in groups of three, and read back with the
///     same marks whatever the reader's language says a comma is.
/// </summary>
/// <remarks>The grouping as it is typed is the runtime's (<c>maskMoney</c>, <c>RuntimeHookShapingTests</c>).</remarks>
public partial class UiInputMoneyTests : global::Rask.Core.RaskMarkup
{
    private sealed class Invoice
    {
        public decimal Amount { get; set; }

        public decimal? Tip { get; set; }
    }

    [Fact]
    public void A_money_input_shows_the_amount_grouped_in_a_text_field()
    {
        var input = Page.Render(Ui.Input.Value(1234.56m).Money()).Find("input");

        var (value, type, marks) = (input.Attribute("value"), input.Attribute("type"), input.Attribute("data-rask-mask-money"));

        Assert.Equal("1,234.56", value);
        Assert.Equal("text", type);
        Assert.Equal(".,2", marks);
    }

    [Fact]
    public void A_money_input_with_marks_of_its_own_shows_the_amount_with_them()
    {
        var input = Page.Render(Ui.Input.Value(1234567.5m).Money(decimals: ",", thousands: " ")).Find("input");

        var (value, marks) = (input.Attribute("value"), input.Attribute("data-rask-mask-money"));

        Assert.Equal("1 234 567,5", value);
        Assert.Equal(", 2", marks);
    }

    [Fact]
    public async Task A_bound_amount_receives_the_number_its_grouped_text_spells()
    {
        var model = new Invoice();
        var page = Page.Render(() => Form.Model(model)[Ui.Input.Bind(() => model.Amount).Label("Amount").Id("amount").Money()]);

        await page.On("#amount").Change("1,234,567");

        Assert.Equal(1234567m, model.Amount);
    }

    [Fact]
    public async Task A_bound_amount_is_read_with_its_own_marks_and_not_the_readers_language()
    {
        var model = new Invoice();
        var page = Page.Render(() => Form.Model(model)[
            Ui.Input.Bind(() => model.Amount).Label("Amount").Id("amount").Money(decimals: ",", thousands: " ")]);
        var (before, beforeUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("en-US"));

        await page.On("#amount").Change("1 234,56");
        (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (before, beforeUi);

        Assert.Equal(1234.56m, model.Amount);
        Assert.Equal("1 234,56", page.Find("#amount").Attribute("value"));
    }

    [Fact]
    public async Task Text_that_is_no_amount_leaves_the_model_and_says_so_under_the_field()
    {
        var model = new Invoice { Amount = 5m };
        var page = Page.Render(() => Form.Model(model)[Ui.Input.Bind(() => model.Amount).Label("Amount").Id("amount").Money()]);

        await page.On("#amount").Change("12,34");

        Assert.Equal(5m, model.Amount);
        Assert.Equal("Enter a number.", page.TextOf("#amount-error"));
    }

    [Fact]
    public async Task An_emptied_nullable_amount_is_nothing()
    {
        var model = new Invoice { Tip = 3m };
        var page = Page.Render(() => Form.Model(model)[Ui.Input.Bind(() => model.Tip).Label("Tip").Id("tip").Money()]);

        await page.On("#tip").Change("");

        Assert.Null(model.Tip);
    }

    [Fact]
    public async Task A_controlled_amount_hands_the_parent_the_number()
    {
        decimal? got = null;
        var page = Page.Render(Ui.Input.Value(0m).Money().Id("amount").OnChange(value => got = value));

        await page.On("#amount").Change("1,234.5");

        Assert.Equal(1234.5m, got);
    }

    [Theory]
    [InlineData("", ",")]
    [InlineData("..", ",")]
    [InlineData(".", ".")]
    public void Money_takes_two_different_marks_of_one_character_each(string decimals, string thousands)
    {
        var input = Ui.Input.Of<decimal>();

        var refused = Record.Exception(() => input.Money(decimals, thousands));

        Assert.IsAssignableFrom<ArgumentException>(refused);
    }
}
