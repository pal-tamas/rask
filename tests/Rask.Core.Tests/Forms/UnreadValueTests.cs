using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// What a bound number, date or time field makes of text it cannot read: the model keeps what it held, and the
// form says under the field what was wrong with what was typed — ahead of "required" and of the app's own rule,
// until the reader corrects it. Plain Core controls; the kit's input draws one of these (UiUnreadValueTests).
public partial class UnreadValueTests : global::Rask.Core.RaskMarkup
{
    private const string Whole = "Enter a whole number.";
    private const string Number = "Enter a number.";
    private const string Range = "The number is out of range.";
    private const string Date = "Enter a valid date.";
    private const string Time = "Enter a valid time.";

    private static Page Field(Tank m, string member, Action<EditContext> probe) => Page.Render(() => Form.Model(m)[
        member switch
        {
            nameof(Tank.Weight) => Input.Bind(() => m.Weight),
            nameof(Tank.Count) => Input.Bind(() => m.Count),
            nameof(Tank.Valves) => Input.Bind(() => m.Valves),
            nameof(Tank.Price) => Input.Bind(() => m.Price),
            nameof(Tank.Ratio) => Input.Bind(() => m.Ratio),
            nameof(Tank.Day) => Input.Bind(() => m.Day),
            nameof(Tank.At) => Input.Bind(() => m.At),
            _ => (Component)Input.Bind(() => m.When),
        },
        Test.EditContextProbe(probe)
    ]);

    [Theory]
    [InlineData(nameof(Tank.Weight), "1.5", Whole)]
    [InlineData(nameof(Tank.Weight), "1.5e0", Whole)]
    [InlineData(nameof(Tank.Weight), "abc", Whole)]
    [InlineData(nameof(Tank.Weight), "-", Whole)]
    [InlineData(nameof(Tank.Weight), "99999999999", Range)]
    [InlineData(nameof(Tank.Weight), "1e30", Range)]
    [InlineData(nameof(Tank.Count), "1.5", Whole)]
    [InlineData(nameof(Tank.Count), "99999999999999999999", Range)]
    [InlineData(nameof(Tank.Count), "999999999999999999999999999999999", Range)]
    [InlineData(nameof(Tank.Valves), "-5", Range)]
    [InlineData(nameof(Tank.Valves), "0.5", Whole)]
    [InlineData(nameof(Tank.Price), "abc", Number)]
    [InlineData(nameof(Tank.Price), "1e30", Range)]
    [InlineData(nameof(Tank.Ratio), "abc", Number)]
    [InlineData(nameof(Tank.Ratio), "1e400", Range)]
    [InlineData(nameof(Tank.Day), "2026-13-45", Date)]
    [InlineData(nameof(Tank.When), "tomorrow", Date)]
    [InlineData(nameof(Tank.At), "25:99", Time)]
    public async Task Text_the_members_type_cannot_hold_leaves_the_model_alone_and_says_what_it_is(
        string member, string typed, string sentence)
    {
        var m = new Tank();
        var before = m.ToString();
        EditContext? ctx = null;
        var page = Field(m, member, c => ctx = c);

        await page.On("input").Change(typed);

        Assert.Equal(before, m.ToString());
        Assert.Equal([sentence], ctx!.GetValidationMessages());
        Assert.Equal("true", page.Attr("aria-invalid"));
    }

    [Fact]
    public async Task A_number_written_with_an_exponent_is_read_into_a_whole_number_and_a_decimal()
    {
        var m = new Tank();
        var weight = Field(m, nameof(Tank.Weight), _ => { });
        var count = Field(m, nameof(Tank.Count), _ => { });
        var price = Field(m, nameof(Tank.Price), _ => { });

        await weight.On("input").Change("1e3");
        await count.On("input").Change("2E2");
        await price.On("input").Change("15e-1");

        Assert.Equal(1000, m.Weight);
        Assert.Equal(200, m.Count);
        Assert.Equal(1.5m, m.Price);
    }

    [Fact]
    public async Task An_empty_field_is_still_empty_and_a_required_one_still_says_so()
    {
        var m = new Tank();
        EditContext? ctx = null;
        var page = Field(m, nameof(Tank.Weight), c => ctx = c);

        await page.On("input").Change("");

        Assert.Null(m.Weight);
        Assert.Equal(["The Weight field is required."], ctx!.GetValidationMessages());
    }

    [Fact]
    public async Task What_could_not_be_read_is_said_ahead_of_required_and_the_save_does_not_run()
    {
        var m = new Tank { Weight = null };
        var saved = 0;
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            Input.Bind(() => m.Weight), Test.EditContextProbe(c => ctx = c)]);

        await page.On("input").Change("1.5");
        await page.On("form").Submit();
        await page.On("form").Submit();

        Assert.Equal([Whole], ctx!.GetValidationMessages());
        Assert.Equal(0, saved);
        Assert.Null(m.Weight);
    }

    [Fact]
    public async Task The_apps_own_rule_is_not_asked_about_a_value_the_field_never_read()
    {
        var m = new Tank();
        var asked = new List<int?>();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => { })[
            Input.Bind(() => m.Weight).Validate(v =>
            {
                asked.Add(v);
                return ["never under ten"];
            }),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Change("1.5");
        await page.On("form").Submit();
        var whileUnread = ctx!.GetValidationMessages().ToArray();
        await page.On("input").Change("12");

        Assert.Equal([Whole], whileUnread);
        Assert.Equal([12], asked);
        Assert.Equal(["never under ten"], ctx.GetValidationMessages());
    }

    [Fact]
    public async Task It_is_said_on_a_form_that_validates_nothing_by_itself()
    {
        var m = new Tank();
        var saved = 0;
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m).AutoValidate(false).OnSubmit(_ => saved++)[
            Input.Bind(() => m.Weight), Test.EditContextProbe(c => ctx = c)]);

        await page.On("input").Change("1.5");
        await page.On("form").Submit();

        Assert.Equal([Whole], ctx!.GetValidationMessages());
        Assert.Equal(0, saved);
    }

    [Fact]
    public async Task The_first_keystroke_of_a_correction_takes_the_sentence_away_and_the_corrected_value_saves()
    {
        var m = new Tank();
        var saved = 0;
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            Input.Bind(() => m.Weight), Test.EditContextProbe(c => ctx = c)]);
        await page.On("input").Change("1.5");
        var asksForTheFirstEdit = page.HandlerId("edit");

        await page.Invoke(asksForTheFirstEdit!, """{"type":"edit"}""");
        var afterTheFirstKey = ctx!.GetValidationMessages().ToArray();
        await page.On("input").Change("2");
        await page.On("form").Submit();

        Assert.Empty(afterTheFirstKey);
        Assert.Null(page.HandlerId("edit"));
        Assert.Equal(2, m.Weight);
        Assert.Equal(1, saved);
    }

    // The case that saved the wrong thing: an edit form whose field already holds a value. The text arrives,
    // does not fit the member, and the stored value must not pass for what was typed.
    [Theory]
    [InlineData(nameof(Tank.Weight), "99999999999", Range)]
    [InlineData(nameof(Tank.Weight), "6500.5", Whole)]
    [InlineData(nameof(Tank.Price), "1e30", Range)]
    [InlineData(nameof(Tank.Price), "12.5.0", Number)]
    [InlineData(nameof(Tank.Day), "2026-02-31", Date)]
    public async Task On_an_edit_form_text_that_does_not_fit_refuses_the_save_and_the_stored_value_is_not_saved_as_typed(
        string member, string typed, string sentence)
    {
        var m = new Tank { Weight = 6500, Price = 27.5m, Day = new DateOnly(2026, 10, 12) };
        var before = m.ToString();
        var saved = new List<string>();
        var invalid = 0;
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m).OnSubmit(t => saved.Add(t.ToString())).OnInvalidSubmit(_ => invalid++)[
            member switch
            {
                nameof(Tank.Weight) => Input.Bind(() => m.Weight),
                nameof(Tank.Price) => Input.Bind(() => m.Price),
                _ => (Component)Input.Bind(() => m.Day),
            },
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Change(typed);
        await page.On("form").Submit();

        Assert.Empty(saved);
        Assert.Equal(1, invalid);
        Assert.Equal([sentence], ctx!.GetValidationMessages());
        Assert.Equal(before, m.ToString());
    }

    [Fact]
    public async Task A_rule_on_the_edit_form_is_not_satisfied_by_the_stored_value_while_the_typed_one_is_unread()
    {
        var m = new Tank { Weight = 6500 };
        var saved = 0;
        var asked = new List<int?>();
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            Input.Bind(() => m.Weight).Validate(v =>
            {
                asked.Add(v);
                return [];
            })
        ]);

        await page.On("input").Change("99999999999");
        await page.On("form").Submit();
        var refused = saved;
        await page.On("input").Change("7000");
        await page.On("form").Submit();

        Assert.Equal(0, refused);
        Assert.Equal(1, saved);
        Assert.Equal(7000, m.Weight);
        Assert.DoesNotContain(6500, asked);
    }

    [Fact]
    public async Task A_field_bound_on_blur_and_one_sent_after_a_pause_refuse_the_save_the_same_way()
    {
        var m = new Tank { Weight = 6500, Count = 3 };
        var saved = 0;
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            Input.Bind(() => m.Weight).Blur().Id("left"),
            Input.Bind(() => m.Count).Debounce(300.Milliseconds).Id("paused"),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("#left").Change("99999999999");
        await page.On("#paused").Input("1.5");
        await page.On("form").Submit();

        Assert.Equal([Range, Whole], ctx!.GetValidationMessages());
        Assert.Equal(0, saved);
        Assert.Equal(6500, m.Weight);
        Assert.Equal(3, m.Count);
    }

    [Fact]
    public async Task Emptying_the_field_after_text_it_could_not_read_is_an_empty_field_again()
    {
        var m = new Tank { Price = 27.5m };
        var saved = 0;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[Input.Bind(() => m.Price)]);

        await page.On("input").Change("1e30");
        await page.On("input").Change("");
        await page.On("form").Submit();

        Assert.Null(m.Price);
        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task A_controlled_input_hears_nothing_of_text_it_cannot_read()
    {
        var heard = new List<int>();
        var page = Page.Render(() => Input.Of<int>().OnChange(heard.Add));

        await page.On("input").Change("1.5");

        Assert.Empty(heard);
    }

    // What the browser sends for a number, date or time input holding text it will not hand over: an empty
    // value, and beside it the input's type (rask-input.ts).
    private static string HeldBack(string input, string type = "change") =>
        $$"""{"type":"{{type}}","value":"","bad":"{{input}}"}""";

    [Theory]
    [InlineData(nameof(Tank.Weight), "number", Whole)]
    [InlineData(nameof(Tank.Count), "number", Whole)]
    [InlineData(nameof(Tank.Price), "number", Number)]
    [InlineData(nameof(Tank.Ratio), "number", Number)]
    [InlineData(nameof(Tank.Day), "date", Date)]
    [InlineData(nameof(Tank.When), "datetime-local", Date)]
    [InlineData(nameof(Tank.At), "time", Time)]
    public async Task Text_the_browser_held_back_is_not_an_empty_field(string member, string input, string sentence)
    {
        var m = new Tank();
        var before = m.ToString();
        EditContext? ctx = null;
        var page = Field(m, member, c => ctx = c);

        await page.On("input").Raise("change", HeldBack(input));

        Assert.Equal(before, m.ToString());
        Assert.Equal([sentence], ctx!.GetValidationMessages());
    }

    [Theory]
    [InlineData("number", Number)]
    [InlineData("date", Date)]
    [InlineData("time", Time)]
    public async Task Text_held_back_behind_a_member_of_another_type_is_answered_by_the_kind_of_input(string input, string sentence)
    {
        var m = new Label();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Code).Type(InputType.Number), Test.EditContextProbe(c => ctx = c)]);

        await page.On("input").Raise("change", HeldBack(input));

        Assert.Equal("x", m.Code);
        Assert.Equal([sentence], ctx!.GetValidationMessages());
    }

    [Fact]
    public async Task A_kind_of_input_nobody_has_a_sentence_for_is_the_empty_field_it_says_it_is()
    {
        var m = new Label();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Code), Test.EditContextProbe(c => ctx = c)]);

        await page.On("input").Raise("change", HeldBack("email"));

        Assert.Equal("", m.Code);
        Assert.Empty(ctx!.GetValidationMessages());
    }

    [Fact]
    public async Task Held_back_text_is_said_ahead_of_required_on_a_new_form_and_the_save_does_not_run()
    {
        var m = new Tank { Weight = null };
        var saved = 0;
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            Input.Bind(() => m.Weight), Test.EditContextProbe(c => ctx = c)]);

        await page.On("input").Raise("change", HeldBack("number"));
        await page.On("form").Submit();

        Assert.Equal([Whole], ctx!.GetValidationMessages());
        Assert.Equal(0, saved);
    }

    [Fact]
    public async Task A_live_field_and_one_bound_on_blur_say_it_as_the_frame_arrives()
    {
        var m = new Tank();
        EditContext? live = null;
        EditContext? blur = null;
        var typed = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Weight).Live(), Test.EditContextProbe(c => live = c)]);
        var left = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Count).Blur(), Test.EditContextProbe(c => blur = c)]);

        await typed.On("input").Raise("input", HeldBack("number", "input"));
        await left.On("input").Raise("change", HeldBack("number"));

        Assert.Equal([Whole], live!.GetValidationMessages());
        Assert.Equal([Whole], blur!.GetValidationMessages());
        Assert.Equal(7, m.Weight);
        Assert.Equal(3, m.Count);
    }

    [Fact]
    public async Task A_controlled_input_hears_nothing_of_text_the_browser_held_back()
    {
        var heard = new List<int>();
        var page = Page.Render(() => Input.Of<int>().OnChange(heard.Add));

        await page.On("input").Raise("change", HeldBack("number"));

        Assert.Empty(heard);
    }

    private sealed class Label
    {
        public string Code { get; set; } = "x";
    }

    private sealed class Tank
    {
        [Required]
        public int? Weight { get; set; } = 7;

        public long Count { get; set; } = 3;

        public uint Valves { get; set; } = 2;

        public decimal? Price { get; set; }

        public double Ratio { get; set; } = 0.5;

        public DateOnly? Day { get; set; } = new(2026, 10, 12);

        public TimeOnly? At { get; set; }

        public DateTime? When { get; set; }

        public override string ToString() => FormattableString.Invariant(
            $"{Weight}|{Count}|{Valves}|{Price}|{Ratio}|{Day:O}|{At:O}|{When:O}");
    }
}
