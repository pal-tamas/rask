using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux UI's OTP input: one real text input per character, and the code they spell kept in one string.
/// </summary>
/// <remarks>
///     Moving focus from cell to cell as a character lands is the browser's job in Flux — its script — and is not
///     here: see <c>docs/ui-kit.md</c>. These hold what the server decides: which characters a code keeps, what a
///     cell's text does to it, and when it is complete.
/// </remarks>
public partial class UiOtpTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Length_draws_that_many_text_inputs_each_named_by_its_place()
    {
        var html = Otp();

        Assert.Equal(6, Occurrences(html, "<input"));
        Assert.Equal(6, Occurrences(html, "data-ui-otp-input"));
        Assert.Contains("aria-label=\"Character 1 of 6\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Character 6 of 6\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"group\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_cell_asks_the_phone_for_the_code_it_just_received()
    {
        var html = Otp();

        Assert.Equal(1, Occurrences(html, "autocomplete=\"one-time-code\""));
        Assert.Equal(5, Occurrences(html, "autocomplete=\"off\""));
        Assert.Equal(6, Occurrences(html, "inputmode=\"numeric\""));
    }

    [Fact]
    public void No_cell_of_a_code_is_carried_across_a_reload() =>
        Assert.Contains("data-rask-no-restore", Otp(), StringComparison.Ordinal);

    [Fact]
    public void Autocomplete_off_stops_the_browser_offering_a_code()
    {
        var html = Ui.Otp.Value("").Length(4).Autocomplete("off").ToHtml();

        Assert.DoesNotContain("one-time-code", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("12", 3)]
    [InlineData("123456", 6)]
    public void Tab_stops_at_the_first_empty_cell_or_the_last_of_a_full_code(string code, int stop)
    {
        var html = Ui.Otp.Value(code).Length(6).ToHtml();

        var stops = Inputs(html).Where(cell => cell.Contains("tabindex=\"0\"", StringComparison.Ordinal)).ToList();

        Assert.Single(stops);
        Assert.Contains($"aria-label=\"Character {stop} of 6\"", stops[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Each_cell_shows_its_own_character()
    {
        var cells = Inputs(Ui.Otp.Value("407").Length(4).ToHtml());

        Assert.Equal(["4", "0", "7", ""], cells.Select(Value));
    }

    [Fact]
    public async Task A_character_typed_in_a_cell_joins_the_bound_code()
    {
        var model = new Login { Code = "12" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(3, 6)).Input("3");

        Assert.Equal("123", model.Code);
    }

    [Fact]
    public async Task A_character_typed_over_a_filled_cell_replaces_it()
    {
        var model = new Login { Code = "123" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(2, 6)).Input("9");

        Assert.Equal("193", model.Code);
    }

    [Fact]
    public async Task A_character_typed_beside_the_one_a_cell_held_replaces_it_too()
    {
        // Without Flux's script the cell's text is not selected on focus, so the new character can land next
        // to the old one instead of over it.
        var model = new Login { Code = "123" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(2, 6)).Input("29");

        Assert.Equal("193", model.Code);
    }

    [Fact]
    public async Task Emptying_a_cell_closes_the_code_up()
    {
        var model = new Login { Code = "12356" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(3, 6)).Input("");

        Assert.Equal("1256", model.Code);
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("12345678", "123456")]
    [InlineData("12 34-56", "123456")]
    [InlineData("ab12", "12")]
    public async Task Text_of_several_characters_in_the_first_cell_is_the_whole_code(string pasted, string expected)
    {
        var model = new Login { Code = "9" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(1, 6)).Input(pasted);

        Assert.Equal(expected, model.Code);
    }

    [Fact]
    public async Task A_code_typed_straight_through_in_one_cell_fills_the_cells_after_it()
    {
        // Nothing moves focus on when a character lands, so the cell's text grows: it is the code from there on.
        var model = new Login { Code = "12" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(3, 6)).Input("3");
        await page.On(Cell(3, 6)).Input("34");
        var html = await page.On(Cell(3, 6)).Input("345");

        Assert.Equal("12345", model.Code);
        Assert.Equal(["1", "2", "3", "4", "5", ""], Inputs(html).Select(Value));
    }

    [Fact]
    public async Task Backspace_in_a_cell_holding_the_rest_of_the_code_shortens_the_code()
    {
        var model = new Login();
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(1, 6)).Input("123");
        await page.On(Cell(1, 6)).Input("12");
        await page.On(Cell(1, 6)).Input("1");

        Assert.Equal("1", model.Code);
    }

    [Fact]
    public async Task A_cell_left_showing_more_than_its_character_is_drawn_afresh_when_focus_leaves_it()
    {
        // The browser keeps what was typed in a focused input whatever is rendered for it, so the cell is
        // replaced rather than corrected: a new key is a new element.
        var model = new Login();
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        var typing = await page.On(Cell(1, 6)).Input("123456");
        var left = await page.On(Cell(1, 6)).Change("123456");

        Assert.Contains("data-rask-key=\"0-0\"", typing, StringComparison.Ordinal);
        Assert.Contains("data-rask-key=\"0-1\"", left, StringComparison.Ordinal);
        Assert.Contains("data-rask-key=\"1-0\"", left, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_numeric_code_takes_no_letter()
    {
        var model = new Login { Code = "12" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Cell(3, 6)).Input("a");

        Assert.Equal("12", model.Code);
    }

    [Theory]
    [InlineData(Ui.OtpMode.Alphanumeric, "a1-b", "A1B")]
    [InlineData(Ui.OtpMode.Alpha, "a1-b", "AB")]
    public async Task Letters_are_taken_by_the_letter_modes_and_upper_cased(Ui.OtpMode mode, string pasted, string expected)
    {
        var model = new Login();
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6).Mode(mode));

        var html = await page.On(Cell(1, 6)).Input(pasted);

        Assert.Equal(expected, model.Code);
        Assert.DoesNotContain("inputmode", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filling_the_last_cell_runs_OnComplete_with_the_code()
    {
        var completed = new List<string>();
        var model = new Login { Code = "12345" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6).OnComplete(completed.Add));

        await page.On(Cell(6, 6)).Input("6");

        Assert.Equal(["123456"], completed);
    }

    [Fact]
    public async Task A_code_still_short_of_its_length_is_not_complete()
    {
        var completed = new List<string>();
        var page = Page.Render(() => Ui.Otp.Value("123").Length(6).OnComplete(completed.Add));

        await page.On(Cell(4, 6)).Input("4");

        Assert.Empty(completed);
    }

    [Fact]
    public async Task A_controlled_code_reports_the_change_and_keeps_its_own()
    {
        string? reported = null;
        var page = Page.Render(() => Ui.Otp.Value("12").Length(6).OnChange(code => { reported = code; }));

        var html = await page.On(Cell(3, 6)).Input("3");

        Assert.Equal("123", reported);
        Assert.DoesNotContain("value=\"3\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_private_code_is_masked_as_a_password_is()
    {
        var html = Ui.Otp.Value("1234").Length(4).Private().ToHtml();

        Assert.Equal(4, Occurrences(html, "type=\"password\""));
    }

    [Fact]
    public void Cells_placed_by_hand_are_counted_through_their_groups()
    {
        var html = Ui.Otp.Value("12345")[
            Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput],
            Ui.OtpSeparator,
            Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput]
        ].ToHtml();

        Assert.Equal(2, Occurrences(html, "data-ui-input-group=\"\""));
        Assert.Equal(["1", "2", "3", "4", "5", ""], Inputs(html).Select(Value));
        Assert.Contains("aria-label=\"Character 4 of 6\"", Inputs(html)[3], StringComparison.Ordinal);
        Assert.Matches("data-ui-text[^>]*>—</p>", System.Net.WebUtility.HtmlDecode(html));
    }

    [Fact]
    public void Length_is_ignored_when_the_cells_are_placed_by_hand()
    {
        var html = Ui.Otp.Value("").Length(6)[Ui.OtpInput, Ui.OtpSeparator, Ui.OtpInput].ToHtml();

        Assert.Equal(2, Occurrences(html, "<input"));
        Assert.Contains("aria-label=\"Character 2 of 2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_names_the_group_and_points_at_the_first_cell()
    {
        var html = Ui.Otp.Value("").Length(4).Label("PIN Code").DescriptionTrailing("Four digits.").ToHtml();

        Assert.Matches("<label [^>]*for=\"f-pin-code\"", html);
        Assert.Contains("id=\"f-pin-code\"", Inputs(html)[0], StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"f-pin-code-label\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-pin-code-description-trailing\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_code_marks_every_cell()
    {
        var html = Ui.Otp.Value("12").Length(4).Invalid().ToHtml();

        Assert.Equal(4, Marked(html, "data-invalid"));
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_code_disables_every_cell()
    {
        var html = Ui.Otp.Value("").Length(4).Disabled().ToHtml();

        Assert.Equal(4, Marked(html, "disabled"));
    }

    private static string Otp() => Ui.Otp.Value("").Length(6).ToHtml();

    private static string Cell(int place, int total) => $"input[aria-label=\"Character {place} of {total}\"]";

    private static int Occurrences(string haystack, string needle)
    {
        var (count, at) = (0, haystack.IndexOf(needle, StringComparison.Ordinal));
        while (at >= 0)
        {
            (count, at) = (count + 1, haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal));
        }

        return count;
    }

    // Every <input> tag, with what the serializer encoded (a `+` in a calc()) decoded again.
    private static List<string> Inputs(string html) =>
        [.. InputTag().Matches(System.Net.WebUtility.HtmlDecode(html)).Select(tag => tag.Value)];

    private static int Marked(string html, string attribute) =>
        System.Text.RegularExpressions.Regex.Matches(html, $"\\s{attribute}[\\s=/>]").Count;

    [System.Text.RegularExpressions.GeneratedRegex("<input[^>]*>")]
    private static partial System.Text.RegularExpressions.Regex InputTag();

    private static string Value(string cell) =>
        System.Text.RegularExpressions.Regex.Match(cell, " value=\"([^\"]*)\"").Groups[1].Value;

    private sealed class Login
    {
        public string Code { get; set; } = "";
    }
}
