namespace Rask.UiTests.Components;

/// <summary>
///     What can live INSIDE a text field's box: an icon, a shortcut, a button that empties it.
/// </summary>
/// <remarks>
///     Flux UI's input affordances. The interesting part is not that they render — it is what has to give way
///     for them: the box stops being the <c>&lt;input&gt;</c> and becomes a container around it, and the
///     floating caption, which rises through exactly that room, steps aside.
/// </remarks>
public partial class UiInputAffordanceTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_plain_field_is_still_a_bare_input()
    {
        // Nothing asked for, nothing added: the markup for the common case is what it always was.
        var html = Ui.Input.Value("").Label("Email").ToHtml();

        Assert.DoesNotContain("<div class=\"input", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_moves_the_box_out_to_a_container()
    {
        // daisyUI's icon input: the BOX holds the icon and the input beside it, so the input itself goes bare.
        var html = Ui.Input.Value("").Label("Search").Icon(Ui.IconName.Search).ToHtml();

        Assert.Contains("<div class=\"input", html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains("class=\"validator grow\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_container_is_a_div_rather_than_a_label()
    {
        // A wrapping <label> implicitly names the input it holds, and the field already has a label — two
        // names on one control is what produced "Email Email" the last time this happened.
        var html = Ui.Input.Value("").Label("Search").Icon(Ui.IconName.Search).ToHtml();

        Assert.Equal(1, Occurrences(html, "<label"));
    }

    [Fact]
    public void A_field_with_something_in_its_box_keeps_its_label_above_it()
    {
        // The floating caption rises through the inside of the box, which is where the icon now sits. Stating
        // Floating(true) beside an icon is a contradiction rather than a preference.
        var floating = Ui.Input.Value("").Label("Search").ToHtml();
        var withIcon = Ui.Input.Value("").Label("Search").Icon(Ui.IconName.Search).Floating(true).ToHtml();

        Assert.Contains("floating-label", floating, StringComparison.Ordinal);
        Assert.DoesNotContain("floating-label", withIcon, StringComparison.Ordinal);
        Assert.Contains("fieldset-legend", withIcon, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shortcut_is_shown_at_the_end_of_the_box() =>
        Assert.Contains("kbd",
            Ui.Input.Value("").Label("Search").Kbd("⌘K").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Clearing_is_offered_only_when_there_is_something_to_clear()
    {
        Assert.Contains("aria-label=\"Clear Search\"",
            Ui.Input.Value("rask").Label("Search").Clearable(true).ToHtml(), StringComparison.Ordinal);

        Assert.DoesNotContain("aria-label=\"Clear Search\"",
            Ui.Input.Value("").Label("Search").Clearable(true).ToHtml(), StringComparison.Ordinal);

        // A disabled field is not one to change.
        Assert.DoesNotContain("aria-label=\"Clear Search\"",
            Ui.Input.Value("rask").Label("Search").Clearable(true).Disabled(true).ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_empties_the_bound_member()
    {
        var model = new Query { Text = "rask" };
        var page = global::Rask.Testing.RaskTest.Render(
            Ui.Input.Bind(() => model.Text).Label("Search").Clearable(true));

        await page.On("button[aria-label=\"Clear Search\"]").ClickAsync();

        Assert.True(string.IsNullOrEmpty(model.Text));
    }

    [Fact]
    public void An_icon_field_still_carries_the_fields_own_aria()
    {
        // The affordances must not cost the field what UiFormField gives every control — the hint it is
        // described by, and the invalid state that reveals the message.
        var html = Ui.Input.Value("x").Label("Search").Icon(Ui.IconName.Search)
            .Hint("Try a package name.").Tone(Ui.Tone.Error).Error("No such package.").ToHtml();

        Assert.Contains("aria-describedby=", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
    }

    private sealed class Query
    {
        public string Text { get; set; } = "";
    }

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
