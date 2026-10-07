using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>Flux's input group, its prefix and suffix, and the mask pattern an input formats with.</summary>
public partial class UiInputGroupTests : global::Rask.Core.RaskMarkup
{
    private sealed class Site
    {
        public string Website { get; set; } = "";
    }

    [Fact]
    public void A_group_is_a_row_marked_for_its_children_to_fuse_in()
    {
        var group = Ui.InputGroup[Ui.InputGroupPrefix["https://"], Ui.Input.Of<string>().Placeholder("example.com")];

        var html = group.ToHtml();

        Assert.StartsWith("<div class=\"flex w-full ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-input-group=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-input-group-prefix=\"\">https://</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prefix_keeps_its_leading_corners_and_a_suffix_its_trailing_ones()
    {
        var (prefix, suffix) = (Ui.InputGroupPrefix["$"], Ui.InputGroupSuffix[".com"]);

        var (before, after) = (prefix.ToHtml(), suffix.ToHtml());

        Assert.Contains("border-e-0 rounded-s-lg", before, StringComparison.Ordinal);
        Assert.Contains("border-s-0 rounded-e-lg", after, StringComparison.Ordinal);
        Assert.Contains("data-ui-input-group-suffix", after, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.InputSize.Sm, "text-sm")]
    [InlineData(Ui.InputSize.Xs, "text-xs")]
    public void A_prefix_takes_the_size_of_the_input_beside_it(Ui.InputSize size, string text)
    {
        var prefix = Ui.InputGroupPrefix.Size(size)["$"];

        var html = prefix.ToHtml();

        Assert.Matches($"class=\"[^\"]*\\b{text}(\\s|\")", html);
    }

    [Fact]
    public void A_bound_input_in_a_group_draws_no_field_of_its_own()
    {
        var model = new Site();

        var html = Page.Render(() => Form.Model(model)[
            Ui.InputGroup[Ui.InputGroupPrefix["https://"], Ui.Input.Bind(() => model.Website)]
        ]).Html;

        Assert.DoesNotContain("data-ui-field", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-input-group=\"\"><div", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_around_a_group_labels_the_input_inside_and_shows_its_error()
    {
        var model = new Site();
        var form = new EditContext(model);
        form.AddValidationMessage(new FieldIdentifier(model, nameof(Site.Website)), "Not a domain.");

        var html = Page.Render(() => Form.Model(model).Context(form)[
            Ui.Field[
                Ui.Label["Website"],
                Ui.InputGroup[Ui.InputGroupPrefix["https://"], Ui.Input.Bind(() => model.Website)],
                Ui.Error
            ]
        ]).Html;

        Assert.Contains("for=\"f-website\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"f-website\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-website-error\"", html, StringComparison.Ordinal);
        Assert.Contains("Not a domain.", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(999) 999-9999", "7161234567", "(716) 123-4567")]
    [InlineData("(999) 999-9999", "(716) 123-4567", "(716) 123-4567")]
    [InlineData("(999) 999-9999", "716", "(716")]
    [InlineData("(999) 999-9999", "7161", "(716) 1")]
    [InlineData("(999) 999-9999", "71a6-12.34567899", "(716) 123-4567")]
    [InlineData("99/99/9999", "01022026", "01/02/2026")]
    [InlineData("aa-999", "4hu77123", "hu-477")]
    [InlineData("***-***", "a1b2c3d4", "a1b-2c3")]
    [InlineData("9999", "", "")]
    public void A_mask_takes_what_fits_its_slots_and_writes_its_own_characters(string mask, string typed, string expected)
    {
        var formatted = UiInputMask.Format(mask, typed);

        Assert.Equal(expected, formatted);
    }
}
