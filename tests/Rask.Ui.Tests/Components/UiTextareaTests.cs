using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>Flux's textarea: its binding, its field, and what <c>Rows</c> and <c>Resize</c> write.</summary>
/// <remarks>The look is held to fluxui.dev by <c>TextareaParity</c>.</remarks>
public partial class UiTextareaTests : global::Rask.Core.RaskMarkup
{
    private sealed class Order
    {
        public string Notes { get; set; } = "No onion.";
    }

    [Fact]
    public void A_textarea_is_the_element_itself_four_lines_tall_and_resizable_vertically()
    {
        var textarea = Ui.Textarea.Of<string>();

        var html = textarea.ToHtml();

        Assert.StartsWith("<textarea", html, StringComparison.Ordinal);
        Assert.Contains("rows=\"4\"", html, StringComparison.Ordinal);
        Assert.Contains("resize-y", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-textarea", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-control", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_textarea_draws_the_model_and_names_the_member()
    {
        var model = new Order();

        var html = Ui.Textarea.Bind(() => model.Notes).Label("Order notes").ToHtml();

        Assert.Contains(">No onion.</textarea>", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Notes\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"f-notes\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-notes\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_is_typed_into_a_bound_textarea_reaches_the_model_with_the_next_action()
    {
        var model = new Order();
        var page = Page.Render(() => Form.Model(model)[
            Ui.Textarea.Bind(() => model.Notes).Label("Order notes"),
            Button.OnClick(() => { })["Save"]
        ]);
        await page.Type("Extra pickles.").Into("Order notes");

        await page.Click("Save");

        Assert.Equal("Extra pickles.", model.Notes);
    }

    [Fact]
    public void A_label_a_description_and_a_badge_wrap_it_in_a_field()
    {
        var textarea = Ui.Textarea.Of<string>().Label("Bio").Description("A few words.").Badge("Optional");

        var html = textarea.ToHtml();

        Assert.Contains("data-ui-field", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-bio-description\"", html, StringComparison.Ordinal);
        Assert.Contains("Optional", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_sets_the_number_of_lines()
    {
        var textarea = Ui.Textarea.Of<string>().Rows(2);

        var html = textarea.ToHtml();

        Assert.Contains("rows=\"2\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("field-sizing-content", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_rows_grow_with_the_content_by_css_and_write_no_rows()
    {
        var textarea = Ui.Textarea.Of<string>().Rows(UiTextareaRows.Auto);

        var html = textarea.ToHtml();

        Assert.Contains("field-sizing-content", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rows=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-input", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.TextareaResize.Vertical, "resize-y")]
    [InlineData(Ui.TextareaResize.Horizontal, "resize-x")]
    [InlineData(Ui.TextareaResize.Both, "resize")]
    [InlineData(Ui.TextareaResize.None, "resize-none")]
    public void Each_resize_value_writes_its_own_utility(Ui.TextareaResize resize, string utility)
    {
        var textarea = Ui.Textarea.Of<string>().Resize(resize);

        var html = textarea.ToHtml();

        Assert.Matches($"class=\"[^\"]*\\b{utility}(\\s|\")", html);
    }

    [Fact]
    public void Invalid_disabled_and_read_only_reach_the_element()
    {
        var textarea = Ui.Textarea.Value("x").Invalid().Disabled().ReadOnly();

        var html = textarea.ToHtml();

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Matches("<textarea[^>]* data-invalid", html);
        Assert.Matches("<textarea[^>]* disabled", html);
        Assert.Matches("<textarea[^>]* readonly", html);
    }
}
