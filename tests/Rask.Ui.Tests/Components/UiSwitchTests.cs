using Rask.Core;
using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>Flux's switch: what it binds, what it tells assistive tech, and which side of its label it sits on.</summary>
/// <remarks>The look is held to fluxui.dev by <c>SwitchParity</c>; these hold what a class string cannot say.</remarks>
public partial class UiSwitchTests : global::Rask.Core.RaskMarkup
{
    private sealed class Settings
    {
        public bool Alerts { get; set; }
    }

    [Fact]
    public void A_switch_is_a_label_around_a_real_checkbox_that_says_it_is_a_switch()
    {
        var html = Ui.Switch.Value(false).ToHtml();

        Assert.StartsWith("<label", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-switch=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"switch\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Value_is_the_state_of_a_switch_nothing_is_bound_to(bool on)
    {
        var html = Ui.Switch.Value(on).Label("Email alerts").ToHtml();

        Assert.Equal(on, html.Contains(" checked", StringComparison.Ordinal));
        Assert.Equal(on, html.Contains("data-checked=\"\"", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unbound_switch_posts_the_browsers_own_value_rather_than_a_bools_text()
    {
        var html = Ui.Switch.Value(true).Name("alerts").ToHtml();

        Assert.Contains("name=\"alerts\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Flipping_a_controlled_switch_reports_the_new_state()
    {
        bool? seen = null;
        var page = Page.Render(() => Ui.Switch.Value(false).Id("alerts").OnChange(on => seen = on));

        await page.On("#alerts").Change("true");

        Assert.True(seen);
    }

    [Fact]
    public async Task Flipping_a_bound_switch_writes_the_model()
    {
        var model = new Settings();
        var page = Page.Render(() => Form.Model(model)[Ui.Switch.Bind(() => model.Alerts).Label("Email alerts")]);

        await page.On("#f-alerts").Change("true");

        Assert.True(model.Alerts);
    }

    [Fact]
    public void The_switch_sits_after_its_label_and_description_unless_aligned_left()
    {
        var right = Ui.Switch.Value(false).Label("Email alerts").Description("Sent at most once a day.").ToHtml();
        var left = Ui.Switch.Value(false).Label("Email alerts").Left.ToHtml();

        Assert.True(At(right, "data-ui-label=\"\"") < At(right, "data-ui-description=\"\""));
        Assert.True(At(right, "data-ui-description=\"\"") < At(right, "data-ui-switch=\"\""));
        Assert.True(At(left, "data-ui-switch=\"\"") < At(left, "data-ui-label=\"\""));
    }

    [Fact]
    public void The_label_names_the_input_and_the_description_describes_it()
    {
        var html = Ui.Switch.Value(false).Label("Email alerts").Description("Sent at most once a day.").ToHtml();

        Assert.Contains("id=\"f-email-alerts\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-email-alerts\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-email-alerts-description\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_switch_disables_the_input_itself()
    {
        var html = Ui.Switch.Value(true).Label("Email alerts").Disabled().ToHtml();

        Assert.Contains(" disabled", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_switch_the_form_rejects_is_invalid_and_described_by_its_message()
    {
        var model = new Settings();
        var form = new EditContext(model);
        form.AddValidationMessage(new FieldIdentifier(model, nameof(Settings.Alerts)), "Alerts are required here.");

        var html = Page.Render(() => Form.Model(model).Context(form)[Ui.Switch.Bind(() => model.Alerts).Label("Email alerts")]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-alerts-error\"", html, StringComparison.Ordinal);
        Assert.Contains("Alerts are required here.", html, StringComparison.Ordinal);
    }

    private static int At(string html, string marker) => html.IndexOf(marker, StringComparison.Ordinal);

    [Fact]
    public void The_stylesheet_no_longer_carries_the_daisy_controls_these_replaced()
    {
        var css = UiStylesheet.Css;

        // The bare words stand in the kit's comments and would bring the classes back; the plugin's `exclude` keeps them out.
        // daisyUI's aura still names `.toggle` inside a selector of its own, so it is the rules that are looked for.
        Assert.DoesNotContain(".toggle{", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".toggle:", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".radio", css, StringComparison.Ordinal);
    }
}
