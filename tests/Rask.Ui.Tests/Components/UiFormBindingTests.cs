using Rask.Core.Forms;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     Every control in the kit's Data input category is an <c>IFormControl&lt;T&gt;</c>, so every one
///     of them binds.
/// </summary>
/// <remarks>
///     The assertion that matters in each case is that the MODEL, not a separate <c>Value</c>, is what
///     the markup draws — a bound control with a second source of truth is the bug this shape exists to
///     make impossible.
/// </remarks>
public partial class UiFormBindingTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void A_bound_input_draws_the_model()
    {
        var model = new Profile { Email = "ada@example.com" };

        Assert.Contains("value=\"ada@example.com\"",
            UiInput.Bind(() => model.Email).Label("Email").ToHtml());
    }

    [Fact]
    public void A_bound_input_names_the_field_so_a_plain_form_posts_it()
    {
        // The framework derives it from the expression: a bound control needs no Name of its own.
        var model = new Profile();

        Assert.Contains("name=\"Email\"", UiInput.Bind(() => model.Email).Label("Email").ToHtml());
    }

    [Fact]
    public void A_bound_input_of_a_non_string_picks_its_own_input_type()
    {
        // T comes off the expression, and the type attribute comes off T — which is the whole point of
        // making these generic rather than string-only.
        var model = new Profile { Age = 36 };
        var html = UiInput.Bind(() => model.Age).Label("Age").ToHtml();

        Assert.Contains("type=\"number\"", html);
        Assert.Contains("value=\"36\"", html);
    }

    [Fact]
    public void A_bound_textarea_draws_the_model()
    {
        var model = new Profile { Notes = "Anything else" };

        Assert.Contains("Anything else", UiTextarea.Bind(() => model.Notes).Label("Notes").ToHtml());
    }

    [Fact]
    public void A_bound_select_marks_the_models_option_as_the_chosen_one()
    {
        var model = new Profile { Country = "gb" };
        var html = UiSelect.Bind(() => model.Country)
            .Options([("hu", "Hungary"), ("gb", "United Kingdom")])
            .Label("Country")
            .ToHtml();

        Assert.Contains("value=\"gb\" selected", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_bound_checkbox_takes_its_checked_state_from_the_model(bool agreed)
    {
        var model = new Profile { Agreed = agreed };
        var html = UiCheckbox.Bind(() => model.Agreed).Text("I agree").ToHtml();

        Assert.Equal(agreed, html.Contains("checked", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bound_toggle_takes_its_checked_state_from_the_model() =>
        Assert.Contains("checked", Toggle(alerts: true));

    [Fact]
    public void A_bound_toggle_that_is_off_writes_no_checked_attribute() =>
        Assert.DoesNotContain("checked", Toggle(alerts: false));

    [Fact]
    public void A_bound_radio_takes_its_own_checked_state_from_the_model()
    {
        // Its OWN state: a single radio binds whether this option is the chosen one, not the group's
        // value. UiFilter<T> is the control that binds the value of a whole group.
        var model = new Profile { Express = true };

        Assert.Contains("checked",
            UiRadio.Bind(() => model.Express).Text("Express").Group("shipping").ToHtml());
    }

    [Fact]
    public void A_bound_range_draws_the_model()
    {
        var model = new Profile { Volume = 40 };

        Assert.Contains("value=\"40\"", UiRange.Bind(() => model.Volume).Label("Volume").ToHtml());
    }

    [Fact]
    public void A_bound_rating_lights_as_many_stars_as_the_model_says()
    {
        var model = new Profile { Stars = 3 };
        var html = UiRating.Bind(() => model.Stars).Group("score").Label("Rate this").Max(5).ToHtml();

        // One checked radio, and it is the third star — a rating group is exclusive, so a second one
        // would mean the browser and the model disagree about the value.
        Assert.Equal(1, Occurrences(html, "checked"));
        Assert.Contains("value=\"3\" checked", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrated_bound_rating_checks_the_hidden_option()
    {
        // Without it a rating can be raised and lowered but never cleared: a radio group offers no way
        // back to none.
        var html = Rating(stars: 0);

        Assert.Contains("value=\"0\" checked", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "checked"));
    }

    [Fact]
    public void A_bound_otp_draws_the_model()
    {
        var model = new Profile { Code = "1234" };

        Assert.Contains("value=\"1234\"",
            UiOtp.Bind(() => model.Code).Label("Verification code").Length(6).ToHtml());
    }

    [Fact]
    public void A_bound_file_input_still_renders_no_value()
    {
        // The one control whose bound mode is write-only: a browser refuses to have a file input's
        // value set, so binding fills the model from the reader's choice and never the other way. A
        // `value` attribute here would be markup the browser drops.
        var model = new Profile { Avatar = "portrait.png" };
        var html = UiFileInput.Bind(() => model.Avatar).Label("Avatar").ToHtml();

        Assert.DoesNotContain("value=", html);
        Assert.DoesNotContain("portrait.png", html);
    }

    [Fact]
    public void A_bound_filter_checks_the_models_option()
    {
        var model = new Profile { Tag = "feature" };
        var html = Filter(model);

        Assert.Contains("value=\"feature\" checked", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "checked"));
    }

    [Fact]
    public void A_bound_filter_with_nothing_chosen_checks_the_reset()
    {
        var model = new Profile { Tag = null };

        Assert.Contains("value=\"\" checked", Filter(model), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_calendar_marks_the_models_day()
    {
        var model = new Profile { Delivery = new DateOnly(2026, 3, 14) };
        var html = UiCalendar.Bind(() => model.Delivery).Label("Delivery date").Month(March).ToHtml();

        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.Equal(1, Occurrences(html, "aria-pressed=\"true\""));
    }

    [Fact]
    public void An_unset_bound_calendar_still_shows_a_month_a_reader_recognises()
    {
        // default(DateOnly) is 1 January year 1. Letting it choose the view would open the grid on a
        // month nobody meant to look at, so an unset value falls back to today.
        var model = new Profile();
        var html = UiCalendar.Bind(() => model.Delivery).Label("Delivery date").ToHtml();

        Assert.Contains(
            DateOnly.FromDateTime(DateTime.Today).ToString("MMMM yyyy",
                System.Globalization.CultureInfo.CurrentCulture),
            html);
        Assert.DoesNotContain("aria-pressed=\"true\"", html);
    }

    // Of<T>() exists on these because a form control's openings are its MODE pins, so its required
    // steps never get to pin the type. Without it a controlled field with no starting value would have
    // to invent one to compile.
    [Fact]
    public void A_field_with_no_value_yet_opens_on_its_type_alone()
    {
        var html = UiInput.Of<string>().Label("Search").Placeholder("Ghost").ToHtml();

        Assert.Contains("placeholder=\"Ghost\"", html);
        Assert.DoesNotContain("value=", html);
    }

    [Fact]
    public void Of_opens_controlled_mode_so_the_parent_still_owns_the_value()
    {
        var seen = "";
        var control = UiInput.Of<string>().Label("Search").OnChange(v => seen = v);

        Assert.Null(control.Bind);
        control.OnChange?.Invoke("typed");
        Assert.Equal("typed", seen);
    }

    [Fact]
    public void Resolve_reads_the_model_when_bound_and_the_property_when_not()
    {
        var model = new Profile { Tag = "docs" };
        var bound = new UiFilter<string> { Group = "t", Options = [], Bind = () => model.Tag };
        var controlled = new UiFilter<string> { Group = "t", Options = [], Value = "bug" };

        Assert.Equal("docs", UiFormCommit.Resolve<string>(bound).Current);
        Assert.Equal("bug", UiFormCommit.Resolve<string>(controlled).Current);
    }

    [Fact]
    public async Task CommitAsync_writes_the_model_back_and_then_runs_AfterBind()
    {
        var model = new Profile { Tag = "bug" };
        var after = "";
        var control = new UiFilter<string>
        {
            Group = "t",
            Options = [],
            Bind = () => model.Tag,
            AfterBind = new Rask.Core.Callback<string>(v => after = v),
        };

        var (acc, ctx, _) = UiFormCommit.Resolve<string>(control);
        await UiFormCommit.CommitAsync(control, acc, ctx, "docs");

        Assert.Equal("docs", model.Tag);
        Assert.Equal("docs", after);
    }

    [Fact]
    public async Task CommitAsync_reports_to_the_parent_when_the_control_is_controlled()
    {
        var seen = "";
        var control = new UiFilter<string> { Group = "t", Options = [], Value = "bug", OnChange = new Rask.Core.Callback<string>(v => seen = v) };

        var (acc, ctx, _) = UiFormCommit.Resolve<string>(control);
        await UiFormCommit.CommitAsync(control, acc, ctx, "docs");

        // Controlled means the parent owns it: nothing here writes Value, it only reports.
        Assert.Equal("docs", seen);
        Assert.Equal("bug", control.Value);
    }

    private static string Toggle(bool alerts)
    {
        var model = new Profile { Alerts = alerts };

        return UiToggle.Bind(() => model.Alerts).Text("Email alerts").ToHtml();
    }

    private static string Rating(int stars)
    {
        var model = new Profile { Stars = stars };

        return UiRating.Bind(() => model.Stars).Group("score").Label("Rate this").Max(5).ToHtml();
    }

    private static string Filter(Profile model) =>
        UiFilter.Bind(() => model.Tag!).Group("tags")
            .Options([("bug", "bug"), ("feature", "feature"), ("docs", "docs")])
            .ToHtml();

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private sealed class Profile
    {
        public string Email { get; set; } = "";

        public int Age { get; set; }

        public string Notes { get; set; } = "";

        public string Country { get; set; } = "";

        public string Code { get; set; } = "";

        public string Avatar { get; set; } = "";

        public string? Tag { get; set; }

        public bool Agreed { get; set; }

        public bool Alerts { get; set; }

        public bool Express { get; set; }

        public double Volume { get; set; }

        public int Stars { get; set; }

        public DateOnly Delivery { get; set; }
    }
}
