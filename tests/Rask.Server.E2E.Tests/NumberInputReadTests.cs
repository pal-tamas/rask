using System.ComponentModel.DataAnnotations;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Text a bound number, date or time input cannot hold, in a real browser over the real socket: the page
///     says under the field what it could not read, nothing is saved, and the first key of a correction takes
///     the sentence away.
/// </summary>
/// <remarks>
///     The browser would refuse these submits itself, with a bubble in its own language, and reports a number
///     field holding a lone minus as empty. The session fails any journey whose page raised an error nothing caught.
/// </remarks>
public sealed class NumberInputReadTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Whole = "Enter a whole number.";
    private const string Number = "Enter a number.";
    private const string Range = "The number is out of range.";
    private const string Date = "Enter a valid date.";
    private const string Time = "Enter a valid time.";
    private const string Untouched = "weight= count=0 price= ratio=0 day= at= when= live= blur=";

    private Task<HookSession> OpenAsync() => HookSession.OpenAsync<NumberReadPage>(playwright);

    // Typed a key at a time over whatever the field held: `fill` refuses text a number input cannot read.
    private static async Task TypeAsync(IPage page, string field, string text)
    {
        var input = page.Locator("#" + field);
        await input.FocusAsync();
        if (!string.IsNullOrEmpty(await input.InputValueAsync()))
        {
            await page.Keyboard.PressAsync("ControlOrMeta+a");
        }

        await input.PressSequentiallyAsync(text);
    }

    [Fact]
    public async Task A_fraction_in_a_whole_number_field_is_told_in_hungarian_and_goes_at_the_first_key_of_the_correction()
    {
        await using var session = await HookSession.OpenAsync<NumberReadPage>(
            playwright, new() { Locale = "hu-HU", ViewportSize = new() { Width = 1000, Height = 700 } },
            cultures: o => o.SupportedCultures.Add("hu"));
        var page = session.Page;

        await TypeAsync(page, "weight", "1.5");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#weight-error")).ToHaveTextAsync("Egész számot adjon meg.");
        await Expect(page.Locator("#weight")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#weight")).ToHaveValueAsync("1.5");
        var savedWhileUnread = await page.Locator("#saved").TextContentAsync();
        await page.Locator("#weight").PressAsync("Backspace");
        await Expect(page.Locator("#weight-error")).ToBeHiddenAsync();
        await page.Locator("#weight").PressAsync("Backspace");
        await page.Locator("#weight").PressSequentiallyAsync("2");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToContainTextAsync("saved=weight=12 ");
        await Expect(page.Locator("#weight")).Not.ToHaveAttributeAsync("aria-invalid", "true");
        Assert.Equal("saved=", savedWhileUnread);
    }

    [Theory]
    [InlineData("weight", "-", Whole)]
    [InlineData("weight", "1e400", Whole)]
    [InlineData("weight", "1.5", Whole)]
    [InlineData("weight", "99999999999", Range)]
    [InlineData("count", "1.5", Whole)]
    [InlineData("count", "99999999999999999999", Range)]
    [InlineData("price", "-", Number)]
    [InlineData("ratio", "1e400", Number)]
    [InlineData("day", "12", Date)]
    [InlineData("when", "12", Date)]
    [InlineData("at", "1", Time)]
    public async Task Save_over_text_a_field_cannot_hold_says_so_under_it_and_saves_nothing(string field, string typed, string sentence)
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await TypeAsync(page, field, typed);
        await page.ClickAsync("#save");

        await Expect(page.Locator("#" + field + "-error")).ToHaveTextAsync(sentence);
        await Expect(page.Locator("#" + field)).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=");
        await Expect(page.Locator("#model")).ToHaveTextAsync(Untouched);
    }

    [Fact]
    public async Task Enter_in_the_field_says_it_as_Save_does_and_a_number_written_with_an_exponent_is_read()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await TypeAsync(page, "weight", "1.5");
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#weight-error")).ToHaveTextAsync(Whole);
        await TypeAsync(page, "weight", "1e3");
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#saved")).ToContainTextAsync("saved=weight=1000 ");
        await Expect(page.Locator("#weight-error")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("live", "-")]
    [InlineData("live", "1.5")]
    [InlineData("blur", "-")]
    [InlineData("blur", "1.5")]
    public async Task A_live_field_says_it_at_its_pause_and_one_bound_on_blur_as_it_is_left(string field, string typed)
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await TypeAsync(page, field, typed);
        await page.Keyboard.PressAsync("Tab");

        await Expect(page.Locator("#" + field + "-error")).ToHaveTextAsync(Whole);
        await Expect(page.Locator("#model")).ToHaveTextAsync(Untouched);
    }

    [Fact]
    public async Task A_required_field_left_empty_still_says_required_and_what_was_typed_elsewhere_is_kept()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await TypeAsync(page, "price", "-");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#weight-error")).ToHaveTextAsync("The Weight field is required.");
        await Expect(page.Locator("#price-error")).ToHaveTextAsync(Number);
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=");
    }
}

/// <summary>A form of every kind of field a number, a date or a time is typed into.</summary>
public sealed partial class NumberReadPage : Component
{
    private readonly Tank _tank = new();
    private string _saved = "";

    protected override Component? HeadAssets => Markup.Title["number read"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        P.Id("model")[Describe()],
        Form.Model(_tank).OnSubmit(Save)[
            Ui.Input.Bind(() => _tank.Weight).Label("Weight").Id("weight"),
            Ui.Input.Bind(() => _tank.Count).Label("Count").Id("count"),
            Ui.Input.Bind(() => _tank.Price).Label("Price").Id("price"),
            Ui.Input.Bind(() => _tank.Ratio).Label("Ratio").Id("ratio"),
            Ui.Input.Bind(() => _tank.Day).Label("Day").Id("day"),
            Ui.Input.Bind(() => _tank.At).Label("At").Id("at"),
            Ui.Input.Bind(() => _tank.When).Label("When").Id("when"),
            Ui.Input.Bind(() => _tank.Live).Live().Label("Live").Id("live"),
            Ui.Input.Bind(() => _tank.Blur).Blur().Label("Blur").Id("blur"),
            Ui.Button.Submit.Id("save")["Save"]
        ]
    ];

    private string Describe() => FormattableString.Invariant(
        $"weight={_tank.Weight} count={_tank.Count} price={_tank.Price} ratio={_tank.Ratio} day={_tank.Day:yyyy-MM-dd} at={_tank.At:HH:mm} when={_tank.When:s} live={_tank.Live} blur={_tank.Blur}");

    private void Save(Tank tank) => _saved = Describe();

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

        public int? Live { get; set; }

        public int? Blur { get; set; }
    }
}
