using Rask.Core.Forms;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux UI's OTP input: one real text input per character, and the code they spell kept in one string.
/// </summary>
/// <remarks>
///     What the keys do inside the cells is the runtime's (<c>data-rask-otp</c>; pinned key by key in
///     <c>RuntimeHookFieldTests</c> and, on the running site, in <c>UiKitDataInputTests</c>). These hold the
///     contract the component owes that hook — cells with no value and no handler, one bound hidden field — and
///     what the server decides: which characters a code keeps, and when it is complete.
/// </remarks>
public partial class UiOtpTests : global::Rask.Core.RaskMarkup
{
    private const string Field = "input[type=\"hidden\"]";

    [Fact]
    public void Length_draws_that_many_text_inputs_each_named_by_its_place()
    {
        var html = Otp();

        Assert.Equal(6, Cells(html).Count);
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

    [Theory]
    [InlineData(Ui.OtpMode.Numeric, "data-rask-otp=\"\"")]
    [InlineData(Ui.OtpMode.Alpha, "data-rask-otp=\"alpha\"")]
    [InlineData(Ui.OtpMode.Alphanumeric, "data-rask-otp=\"alphanumeric\"")]
    public void The_group_asks_the_runtime_for_the_cells_behaviour_in_its_mode(Ui.OtpMode mode, string expected) =>
        Assert.Contains(expected, Ui.Otp.Value("").Length(6).Mode(mode).ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void The_cells_carry_no_value_and_no_handler_and_one_hidden_field_carries_the_code()
    {
        var html = Ui.Otp.Value("407").Length(4).ToHtml();

        Assert.All(Cells(html), cell => Assert.DoesNotContain(" value=", cell, StringComparison.Ordinal));
        Assert.All(Cells(html), cell => Assert.DoesNotContain("data-rask-on", cell, StringComparison.Ordinal));
        Assert.DoesNotContain("data-rask-key", html, StringComparison.Ordinal);
        var hidden = Assert.Single(Inputs(html), input => input.Contains("type=\"hidden\"", StringComparison.Ordinal));
        Assert.Contains("value=\"407\"", hidden, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hidden_field_comes_before_the_cells_inside_the_group() =>
        Assert.Matches("data-rask-otp=\"\"[^>]*><input[^>]*type=\"hidden\"", Otp());

    [Fact]
    public void A_name_is_the_hidden_field_s()
    {
        var html = Ui.Otp.Value("").Length(4).Name("code").ToHtml();

        var named = Assert.Single(Inputs(html), input => input.Contains("name=\"code\"", StringComparison.Ordinal));
        Assert.Contains("type=\"hidden\"", named, StringComparison.Ordinal);
    }

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

        var stops = Cells(html).Where(cell => cell.Contains("tabindex=\"0\"", StringComparison.Ordinal)).ToList();

        Assert.Single(stops);
        Assert.Contains($"aria-label=\"Character {stop} of 6\"", stops[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_code_the_runtime_reports_is_written_to_the_bound_member()
    {
        var model = new Login { Code = "12" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Field).Input("123");

        Assert.Equal("123", model.Code);
    }

    [Fact]
    public async Task What_the_runtime_reported_comes_back_unchanged_so_it_is_known_as_its_own()
    {
        // The runtime ignores a render that says what it announced, and takes anything else for the page
        // changing the code — which would write over the keys typed since.
        var model = new Login();
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6).Alphanumeric);

        var html = await page.On(Field).Input("ab1");

        Assert.Equal("AB1", model.Code);
        Assert.Contains("value=\"ab1\"", Hidden(html), StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_the_page_sets_is_rendered_as_the_code_keeps_it()
    {
        var html = Ui.Otp.Value("ab-1").Length(6).Alphanumeric.ToHtml();

        Assert.Contains("value=\"AB1\"", Hidden(html), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.OtpMode.Numeric, "a1-b2", "12")]
    [InlineData(Ui.OtpMode.Alphanumeric, "a1-b", "A1B")]
    [InlineData(Ui.OtpMode.Alpha, "a1-b", "AB")]
    public async Task The_code_keeps_the_characters_of_its_mode_with_letters_upper_cased(Ui.OtpMode mode, string reported, string expected)
    {
        var model = new Login();
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6).Mode(mode));

        await page.On(Field).Input(reported);

        Assert.Equal(expected, model.Code);
    }

    [Fact]
    public void Only_a_numeric_code_asks_for_the_numeric_keyboard()
    {
        var letters = Ui.Otp.Value("").Length(6).Alphanumeric.ToHtml();

        Assert.DoesNotContain("inputmode", letters, StringComparison.Ordinal);
        Assert.Equal(6, Occurrences(Otp(), "inputmode=\"numeric\""));
    }

    [Fact]
    public async Task A_code_longer_than_its_cells_is_cut_to_them()
    {
        var model = new Login();
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6));

        await page.On(Field).Input("12345678");

        Assert.Equal("123456", model.Code);
    }

    [Fact]
    public async Task Filling_the_last_cell_runs_OnComplete_with_the_code()
    {
        var completed = new List<string>();
        var model = new Login { Code = "12345" };
        var page = Page.Render(() => Ui.Otp.Bind(() => model.Code).Length(6).OnComplete(completed.Add));

        await page.On(Field).Input("123456");

        Assert.Equal(["123456"], completed);
    }

    [Fact]
    public async Task A_code_still_short_of_its_length_is_not_complete()
    {
        var completed = new List<string>();
        var page = Page.Render(() => Ui.Otp.Value("123").Length(6).OnComplete(completed.Add));

        await page.On(Field).Input("1234");

        Assert.Empty(completed);
    }

    [Fact]
    public async Task A_controlled_code_reports_the_change_and_keeps_its_own()
    {
        string? reported = null;
        var page = Page.Render(() => Ui.Otp.Value("12").Length(6).OnChange(code => { reported = code; }));

        var html = await page.On(Field).Input("123");

        Assert.Equal("123", reported);
        Assert.Contains("value=\"12\"", Hidden(html), StringComparison.Ordinal);
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
        Assert.Equal(6, Cells(html).Count);
        Assert.Contains("aria-label=\"Character 4 of 6\"", Cells(html)[3], StringComparison.Ordinal);
        Assert.Matches("data-ui-text[^>]*>—</p>", System.Net.WebUtility.HtmlDecode(html));
    }

    [Fact]
    public void Length_is_ignored_when_the_cells_are_placed_by_hand()
    {
        var html = Ui.Otp.Value("").Length(6)[Ui.OtpInput, Ui.OtpSeparator, Ui.OtpInput].ToHtml();

        Assert.Equal(2, Cells(html).Count);
        Assert.Contains("aria-label=\"Character 2 of 2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_names_the_group_and_points_at_the_first_cell()
    {
        var html = Ui.Otp.Value("").Length(4).Label("PIN Code").DescriptionTrailing("Four digits.").ToHtml();

        Assert.Matches("<label [^>]*for=\"f-pin-code\"", html);
        Assert.Contains("id=\"f-pin-code\"", Cells(html)[0], StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"f-pin-code-label\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-pin-code-description-trailing\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_code_the_form_rejects_marks_every_cell()
    {
        var model = new Login();
        var form = new EditContext(model);
        form.AddValidationMessage(new FieldIdentifier(model, nameof(Login.Code)), "Wrong code.");

        var html = Page.Render(() => Form.Model(model).Context(form)[Ui.Otp.Bind(() => model.Code).Length(4).Label("Code")]).Html;

        Assert.All(Cells(html), cell => Assert.Contains("data-invalid", cell, StringComparison.Ordinal));
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("Wrong code.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_code_disables_every_cell()
    {
        var html = Ui.Otp.Value("").Length(4).Disabled().ToHtml();

        Assert.All(Cells(html), cell => Assert.Matches("\\sdisabled[\\s=/>]", cell));
    }

    private static string Otp() => Ui.Otp.Value("").Length(6).ToHtml();

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

    private static List<string> Cells(string html) =>
        [.. Inputs(html).Where(input => !input.Contains("type=\"hidden\"", StringComparison.Ordinal))];

    private static string Hidden(string html) =>
        Inputs(html).Single(input => input.Contains("type=\"hidden\"", StringComparison.Ordinal));

    private static int Marked(string html, string attribute) =>
        System.Text.RegularExpressions.Regex.Matches(html, $"\\s{attribute}[\\s=/>]").Count;

    [System.Text.RegularExpressions.GeneratedRegex("<input[^>]*>")]
    private static partial System.Text.RegularExpressions.Regex InputTag();

    private sealed class Login
    {
        public string Code { get; set; } = "";
    }
}
