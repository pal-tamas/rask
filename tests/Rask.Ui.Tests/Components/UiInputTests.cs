using Rask.Core;
using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's input: the binding it keeps from <c>Rask.Core</c>'s <c>Input&lt;T&gt;</c>, the field it draws
///     around itself, and the markup contract of everything that sits inside the box.
/// </summary>
/// <remarks>The look is held to fluxui.dev by <c>InputParity</c>; these hold what a class string cannot say.</remarks>
public partial class UiInputTests : global::Rask.Core.RaskMarkup
{
    private sealed class Account
    {
        public string Email { get; set; } = "";

        public string Phone { get; set; } = "";

        public int Age { get; set; } = 41;

        public int? Seats { get; set; }
    }

    private static (Account Model, EditContext Form) Rejected()
    {
        var model = new Account { Email = "a@b.c" };
        var form = new EditContext(model);
        form.AddValidationMessage(new FieldIdentifier(model, nameof(Account.Email)), "That email is taken.");

        return (model, form);
    }

    [Fact]
    public void An_input_is_a_wrapper_holding_the_control()
    {
        var input = Ui.Input.Of<string>().Placeholder("Filter by...");

        var html = input.ToHtml();

        Assert.StartsWith("<div class=\"w-full relative block\" data-ui-input=\"\"><input", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-control", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-group-target", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-field", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Class_reaches_the_wrapper_and_InputClass_the_input()
    {
        var input = Ui.Input.Of<string>().Class("max-w-xs").InputClass("font-mono");

        var html = input.ToHtml();

        Assert.Contains("<div class=\"w-full relative block max-w-xs\"", html, StringComparison.Ordinal);
        Assert.Matches("<input[^>]*class=\"[^\"]*font-mono\"", html);
    }

    [Fact]
    public void A_bound_input_draws_the_model_and_takes_its_type_from_the_member()
    {
        var model = new Account();

        var html = Ui.Input.Bind(() => model.Age).Label("Age").ToHtml();

        Assert.Contains("type=\"number\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"41\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Age\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"f-age\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_member_binds_and_draws_nothing_for_null()
    {
        var model = new Account();

        var html = Ui.Input.Bind(() => model.Seats).Label("Seats").ToHtml();

        Assert.Contains("type=\"number\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typing_into_a_bound_input_writes_the_model()
    {
        var model = new Account();
        var page = Page.Render(() => Form.Model(model)[Ui.Input.Bind(() => model.Phone).Label("Phone")]);

        await page.Type("0612345").Into("Phone");

        Assert.Equal("0612345", model.Phone);
    }

    [Fact]
    public async Task A_controlled_input_reports_to_its_parent()
    {
        var seen = "";
        var input = Ui.Input.Value("a").OnChange(value => seen = value);

        await input.OnChange.Invoke("ab");

        Assert.Null(input.Bind);
        Assert.Equal("ab", seen);
    }

    [Fact]
    public void A_label_wraps_the_input_in_a_field_whose_label_points_at_it()
    {
        var input = Ui.Input.Of<string>().Label("Username").Description("Publicly displayed.");

        var html = input.ToHtml();

        Assert.StartsWith("<div class=\"min-w-0 ", html, StringComparison.Ordinal);
        Assert.Contains("<label id=\"f-username-label\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-username\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-username-description\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_description_sits_under_the_input_and_describes_it()
    {
        var input = Ui.Input.Of<string>().Label("Password").DescriptionTrailing("At least 12 characters.");

        var html = input.ToHtml();

        Assert.True(html.IndexOf("<input", StringComparison.Ordinal) < html.IndexOf("At least 12", StringComparison.Ordinal));
        Assert.Contains("aria-describedby=\"f-password-description-trailing\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_marks_the_control_for_assistive_tech_and_for_the_stylesheet()
    {
        var healthy = Ui.Input.Of<string>();
        var invalid = Ui.Input.Of<string>().Invalid();

        var (plain, marked) = (healthy.ToHtml(), invalid.ToHtml());

        Assert.DoesNotContain("invalid", plain.Replace("data-invalid:", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", marked, StringComparison.Ordinal);
        Assert.Matches("<input[^>]* data-invalid", marked);
    }

    [Fact]
    public void A_bound_input_the_form_rejects_is_invalid_and_described_by_its_message()
    {
        var (model, form) = Rejected();

        var html = Page.Render(() => Form.Model(model).Context(form)[Ui.Input.Bind(() => model.Email).Label("Email")]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-email-error\"", html, StringComparison.Ordinal);
        Assert.Contains("That email is taken.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_input_with_no_label_is_invalid_and_draws_no_field()
    {
        var (model, form) = Rejected();

        var html = Page.Render(() => Form.Model(model).Context(form)[Ui.Input.Bind(() => model.Email)]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-field", html, StringComparison.Ordinal);
        Assert.DoesNotContain("That email is taken.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowValidation_false_keeps_the_input_invalid_and_leaves_the_message_out()
    {
        var (model, form) = Rejected();

        var html = Page.Render(() => Form.Model(model).Context(form)[
            Ui.Input.Bind(() => model.Email).Label("Email").ShowValidation(false)
        ]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("That email is taken.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-describedby", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_and_ReadOnly_write_the_native_attributes()
    {
        var disabled = Ui.Input.Of<string>().Disabled();
        var readOnly = Ui.Input.Value("BA7K").ReadOnly().Filled;

        var (off, locked) = (disabled.ToHtml(), readOnly.ToHtml());

        Assert.Matches("<input[^>]* disabled", off);
        Assert.Matches("<input[^>]* readonly", locked);
        Assert.Contains("bg-zinc-800/5", locked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.InputSize.Base, "h-10")]
    [InlineData(Ui.InputSize.Sm, "h-8")]
    [InlineData(Ui.InputSize.Xs, "h-6")]
    public void Each_size_sets_the_height_of_the_control(Ui.InputSize size, string height)
    {
        var input = Ui.Input.Of<string>().Size(size);

        var html = input.ToHtml();

        Assert.Matches($"<input[^>]*class=\"[^\"]*\\b{height}\\b", html);
    }

    [Fact]
    public void An_icon_sits_before_the_control_and_makes_room_for_itself()
    {
        var input = Ui.Input.Of<string>().Icon(Ui.IconName.MagnifyingGlass);

        var html = input.ToHtml();

        Assert.True(html.IndexOf("<svg", StringComparison.Ordinal) < html.IndexOf("<input", StringComparison.Ordinal));
        Assert.Contains("pointer-events-none", html, StringComparison.Ordinal);
        Assert.Contains("ps-10 pe-3", html, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 20 20\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_icon_follows_the_control_and_can_be_content_of_your_own()
    {
        var named = Ui.Input.Of<string>().IconTrailing(Ui.IconName.CreditCard);
        var own = Ui.Input.Of<string>().IconTrailing(Span.Class("mine")["EUR"]);

        var (icon, content) = (named.ToHtml(), own.ToHtml());

        Assert.True(icon.IndexOf("<input", StringComparison.Ordinal) < icon.IndexOf("<svg", StringComparison.Ordinal));
        Assert.Contains("viewBox=\"0 0 20 20\"", icon, StringComparison.Ordinal);
        Assert.Contains("ps-3 pe-10", icon, StringComparison.Ordinal);
        Assert.Contains("<span class=\"mine\">EUR</span>", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shortcut_is_shown_at_the_end_of_the_box()
    {
        var input = Ui.Input.Of<string>().Kbd("⌘K");

        var html = input.ToHtml();

        Assert.Matches("<input[^>]*>.*<span class=\"pe-2\">[^<]+K</span>", html);
    }

    [Fact]
    public void A_clearable_input_always_carries_its_button_and_hides_it_by_css_while_empty()
    {
        var input = Ui.Input.Value("").Clearable();

        var html = input.ToHtml();

        Assert.Contains("aria-label=\"Clear input\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-clear-button", html, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", html, StringComparison.Ordinal);
        Assert.Contains("[[data-ui-input]:has(input:placeholder-shown)_&amp;]:hidden", html, StringComparison.Ordinal);
        Assert.Contains("placeholder=\" \"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_or_read_only_input_offers_nothing_to_clear()
    {
        var disabled = Ui.Input.Value("x").Clearable().Disabled();
        var readOnly = Ui.Input.Value("x").Clearable().ReadOnly();

        var (off, locked) = (disabled.ToHtml(), readOnly.ToHtml());

        Assert.DoesNotContain("Clear input", off, StringComparison.Ordinal);
        Assert.DoesNotContain("Clear input", locked, StringComparison.Ordinal);
    }

    [Fact]
    public void The_clear_button_names_its_input_for_the_runtime_and_has_no_handler_of_its_own()
    {
        var page = Page.Render(() => Ui.Input.Value("rask").Id("query").Clearable());

        var button = page.Find("button[aria-label=\"Clear input\"]");

        Assert.Equal("query", button.Attribute("data-rask-clear"));
        Assert.Null(button.Attribute("data-rask-on-click"));
    }

    [Fact]
    public void Inputs_nothing_names_have_ids_of_their_own_and_each_clear_button_names_its_input()
    {
        var page = Page.Render(() => Div[Ui.Input.Value("a").Key("a").Clearable(), Ui.Input.Value("b").Key("b").Clearable()]);

        var (inputs, buttons) = (page.FindAll("input"), page.FindAll("button[aria-label=\"Clear input\"]"));

        Assert.NotEqual(inputs[0].Attribute("id"), inputs[1].Attribute("id"));
        Assert.Equal(inputs[0].Attribute("id"), buttons[0].Attribute("data-rask-clear"));
        Assert.Equal(inputs[1].Attribute("id"), buttons[1].Attribute("data-rask-clear"));
    }

    [Fact]
    public void A_copyable_input_ends_in_a_button_the_runtime_copies_from()
    {
        var page = Page.Render(() => Ui.Input.Value("FLUX-1234").Id("key").ReadOnly().Copyable());

        var button = page.Find("button[aria-label=\"Copy to clipboard\"]");

        Assert.Equal("key", button.Attribute("data-rask-copy"));
        Assert.NotNull(button.Attribute("data-ui-button"));
        Assert.Null(button.Attribute("data-rask-on-click"));
    }

    [Fact]
    public void The_copy_button_holds_its_icon_and_the_tick_that_stands_in_while_it_says_it_copied()
    {
        var input = Ui.Input.Value("FLUX-1234").Copyable();

        var html = input.ToHtml();

        Assert.Equal(2, html.Split("data-ui-icon").Length - 1);
        Assert.Contains("hidden in-data-copied:block", html, StringComparison.Ordinal);
        Assert.Contains("in-data-copied:hidden", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Forwarded_attributes_reach_the_input_itself_beside_its_own_marks()
    {
        var page = Page.Render(() => Ui.Input.Value("").Type(InputType.Search).Attributes(("aria-label", "Search keys")));

        var input = page.Find("input");

        Assert.Equal("Search keys", input.Attribute("aria-label"));
        Assert.NotNull(input.Attribute("data-ui-control"));
        Assert.Null(page.Find("[data-ui-input]").Attribute("aria-label"));
    }

    [Fact]
    public void A_masked_input_hands_its_pattern_to_the_runtime()
    {
        var input = Ui.Input.Value("").Mask("(999) 999-9999");

        var html = input.ToHtml();

        Assert.Contains("data-rask-mask=\"(999) 999-9999\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_reveal_button_shows_a_password_and_hides_it_again()
    {
        var page = Page.Render(Ui.Input.Value("hunter2").Type(InputType.Password).Viewable());
        Assert.Contains("type=\"password\"", page.Html, StringComparison.Ordinal);

        await page.On("button[aria-label=\"Toggle password visibility\"]").Click();
        var shown = page.Html;
        await page.On("button[aria-label=\"Toggle password visibility\"]").Click();

        Assert.Contains("type=\"text\"", shown, StringComparison.Ordinal);
        Assert.Contains("data-viewable-open", shown, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-viewable-open", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mask_lays_the_value_into_its_pattern()
    {
        var input = Ui.Input.Value("7161234567").Mask("(999) 999-9999");

        var html = input.ToHtml();

        Assert.Contains("value=\"(716) 123-4567\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_masked_bound_input_commits_the_value_in_its_pattern()
    {
        var model = new Account();
        var page = Page.Render(() => Form.Model(model)[Ui.Input.Bind(() => model.Phone).Label("Phone").Mask("(999) 999-9999")]);

        await page.Type("7161234567").Into("Phone");

        Assert.Equal("(716) 123-4567", model.Phone);
    }

    [Fact]
    public void A_file_input_is_a_label_around_a_hidden_input_a_button_and_the_chosen_name()
    {
        var one = Ui.Input.Of<string>().Type(InputType.File).Label("Logo");
        var many = Ui.Input.Of<string>().Type(InputType.File).Multiple();

        var (single, several) = (one.ToHtml(), many.ToHtml());

        Assert.Contains("<label class=\"relative flex items-center gap-4 cursor-auto\" data-ui-input-file=\"\">", single, StringComparison.Ordinal);
        Assert.Matches("<input[^>]*type=\"file\"[^>]*class=\"sr-only\"|<input[^>]*class=\"sr-only\"[^>]*type=\"file\"", single);
        Assert.Contains("Choose file<", single, StringComparison.Ordinal);
        Assert.Contains("No file chosen", single, StringComparison.Ordinal);
        Assert.DoesNotContain("value=", single, StringComparison.Ordinal);
        Assert.Contains("Choose files<", several, StringComparison.Ordinal);
        Assert.Matches("<input[^>]* multiple", several);
    }

    [Fact]
    public async Task An_input_drawn_as_a_button_is_a_button_that_shows_the_placeholder()
    {
        var pressed = 0;
        var page = Page.Render(
            Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K").OnClick(() => pressed++));

        await page.On("button").Click();

        Assert.StartsWith("<button", page.Html.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("Search...", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<input", page.Html, StringComparison.Ordinal);
        Assert.Equal(1, pressed);
    }
}
