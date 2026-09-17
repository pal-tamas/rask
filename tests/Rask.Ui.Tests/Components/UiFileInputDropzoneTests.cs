namespace Rask.Ui.Tests.Components;

/// <summary>
///     Flux UI's drop area: <c>UiFileInput.Dropzone(true)</c>.
/// </summary>
/// <remarks>
///     The whole design is that it is STILL the native input — stretched invisibly over the area, so a click and
///     a drop both land on it and the browser does what it already does with a file dropped on a file input. So
///     most of what is worth asserting is that the input is there, on top, and still named; the only script
///     involved is the runtime's drag highlight, which the site's browser suite drives.
/// </remarks>
public partial class UiFileInputDropzoneTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_area_is_the_native_input_stretched_over_it()
    {
        var html = UiFileInput.Value("").Label("Receipts").Dropzone(true).ToHtml();

        Assert.Contains("data-rask-dropzone", html, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", html, StringComparison.Ordinal);
        // On top and transparent: a click anywhere is a click on the input, and a drop anywhere is a drop on it.
        Assert.Contains("absolute inset-0 size-full cursor-pointer opacity-0", html, StringComparison.Ordinal);
        // Drawn as an area, not daisyUI's compact box.
        Assert.DoesNotContain("file-input", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_input_stays_named_by_the_label()
    {
        // The input is what a keyboard and a screen reader land on; the words are drawn beside it, not in it.
        var html = UiFileInput.Value("").Label("Receipts").Dropzone(true).Heading("Drop receipts here").ToHtml();

        Assert.Contains("aria-label=\"Receipts\"", html, StringComparison.Ordinal);
        Assert.Contains(">Drop receipts here</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_heading_defaults_to_the_label() =>
        Assert.Contains(">Receipts</p>",
            UiFileInput.Value("").Label("Receipts").Dropzone(true).ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void The_smaller_line_describes_the_input_when_there_is_an_id_to_point_at()
    {
        var html = UiFileInput.Value("").Label("Receipts").Id("receipts").Dropzone(true)
            .Text("PDF or JPG, up to 10 MB").ToHtml();

        Assert.Contains("id=\"receipts-text\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"receipts-text\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_an_id_the_line_is_drawn_but_not_referenced()
    {
        // An aria-describedby naming an id nobody wrote points at nothing, which is worse than no description.
        var html = UiFileInput.Value("").Label("Receipts").Dropzone(true).Text("PDF only").ToHtml();

        Assert.Contains(">PDF only</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-describedby", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_marks_both_the_area_and_the_input()
    {
        var html = UiFileInput.Value("").Label("Receipts").Dropzone(true).Tone(UiTone.Error).ToHtml();

        Assert.Contains("border-error", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_area_refuses_the_drop_with_the_input()
    {
        // A disabled file input already rejects a drop, so disabling the input is the whole of it; the area only
        // stops inviting one.
        var html = UiFileInput.Value("").Label("Receipts").Dropzone(true).Disabled(true).ToHtml();

        Assert.Contains("disabled", html, StringComparison.Ordinal);
        Assert.Contains("cursor-not-allowed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-[dragging]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_compact_box_is_unchanged()
    {
        var html = UiFileInput.Value("").Label("Avatar").ToHtml();

        Assert.Contains("file-input validator", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-dropzone", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".data-\\[dragging\\]\\:border-primary")]
    [InlineData(".has-\\[\\:focus-visible\\]\\:outline-2")]
    public void The_states_the_area_draws_from_are_in_the_shipped_sheet(string selector)
    {
        // Written only in this component, so a class that Tailwind failed to see would leave the drag highlight
        // or the focus ring styled by nothing while the markup looked right.
        Assert.Contains(selector, UiStylesheet.Css, StringComparison.Ordinal);
    }
}
