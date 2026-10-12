using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// What a bound number, date or time field makes of text it cannot read: what the model holds afterwards and
// what the form says under the field. Plain Core controls; the kit's input draws one of these.
public partial class UnreadValueTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("99999999999")]
    [InlineData("abc")]
    public async Task Text_a_whole_number_cannot_hold_leaves_the_model_alone_and_says_nothing(string typed)
    {
        var m = new Tank { Weight = 7 };
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Weight), Test.EditContextProbe(c => ctx = c)]);

        await page.On("input").Change(typed);

        Assert.Equal(7, m.Weight);
        Assert.Empty(ctx!.GetValidationMessages(new FieldIdentifier(m, nameof(Tank.Weight))));
    }

    [Fact]
    public async Task What_the_browser_reports_as_empty_empties_a_required_number_and_calls_it_required()
    {
        var m = new Tank { Weight = 7 };
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Weight), Test.EditContextProbe(c => ctx = c)]);

        await page.On("input").Change("");

        Assert.Null(m.Weight);
        Assert.Equal(["The Weight field is required."], ctx!.GetValidationMessages(new FieldIdentifier(m, nameof(Tank.Weight))));
    }

    [Fact]
    public async Task An_exponent_is_read_into_a_double_and_not_into_a_decimal()
    {
        var m = new Tank();
        var ratio = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Ratio)]);
        var price = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Price)]);

        await ratio.On("input").Change("1e3");
        await price.On("input").Change("1e3");

        Assert.Equal(1000d, m.Ratio);
        Assert.Null(m.Price);
    }

    [Fact]
    public async Task A_date_and_a_time_that_do_not_parse_leave_the_model_alone_and_say_nothing()
    {
        var m = new Tank { Day = new DateOnly(2026, 10, 12), At = new TimeOnly(9, 30) };
        EditContext? ctx = null;
        var day = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Day), Test.EditContextProbe(c => ctx = c)]);
        var at = Page.Render(() => Form.Model(m)[Input.Bind(() => m.At)]);

        await day.On("input").Change("2026-13-45");
        await at.On("input").Change("25:99");

        Assert.Equal(new DateOnly(2026, 10, 12), m.Day);
        Assert.Equal(new TimeOnly(9, 30), m.At);
        Assert.Empty(ctx!.GetValidationMessages(new FieldIdentifier(m, nameof(Tank.Day))));
    }

    private sealed class Tank
    {
        [Required]
        public int? Weight { get; set; }

        public long Count { get; set; }

        public decimal? Price { get; set; }

        public double Ratio { get; set; }

        public DateOnly? Day { get; set; }

        public TimeOnly? At { get; set; }

        public DateTime? When { get; set; }
    }
}
