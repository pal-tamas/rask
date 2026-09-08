namespace Rask.Ui.Tests.Components;

/// <summary>
///     The three decorative data-display components, and the sheet check they need that
///     <c>UiClassNamesTests</c> cannot give them.
/// </summary>
/// <remarks>
///     Their class names are single literals written straight into the component rather than mapped from
///     an enum, so the reflection over <c>UiClassNames</c> never sees them. The failure they are exposed
///     to is the same one — a name absent from the compiled sheet renders unstyled with a green build —
///     so each asserts its own name against <c>UiStylesheet.Css</c> here.
/// </remarks>
public partial class UiDisplayEffectTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_tilt_wraps_its_children() =>
        Assert.Contains("class=\"hover-3d\"", UiHover3d[Span["card"]].ToHtml());

    [Fact]
    public void The_tilt_is_defined_in_the_shipped_sheet() =>
        Assert.Contains(".hover-3d", UiStylesheet.Css, StringComparison.Ordinal);

    [Fact]
    public void The_gallery_is_a_figure()
    {
        // A run of images with no individual caption is what <figure> is for, and daisyUI has a rule
        // specifically for the figure case.
        var html = UiHoverGallery[Span["a"], Span["b"]].ToHtml();

        Assert.StartsWith("<figure", html, StringComparison.Ordinal);
        Assert.Contains("hover-gallery", html);
    }

    [Fact]
    public void The_gallery_is_defined_in_the_shipped_sheet() =>
        Assert.Contains(".hover-gallery", UiStylesheet.Css, StringComparison.Ordinal);

    [Fact]
    public void The_rotator_keeps_the_inner_wrapper_daisyUI_animates()
    {
        // daisyUI counts the children of `> *` to choose the animation, so flattening the extra span
        // away leaves the words stacked and still.
        var html = UiTextRotate.Words(["fast", "typed", "small"]).ToHtml();

        // The nesting is what matters, not the exact bytes: daisyUI animates `.text-rotate > *` and
        // counts THAT element's children, so there must be a wrapper between the class and the words.
        Assert.StartsWith("<span class=\"text-rotate\"><span>", html, StringComparison.Ordinal);
        Assert.EndsWith("</span></span></span>", html, StringComparison.Ordinal);

        // Each word is keyed, so reconciliation moves the spans rather than rewriting their text —
        // which for an element the browser is mid-animation on is the difference between a word
        // changing and the animation restarting.
        Assert.Contains("data-rask-key=\"fast\"", html);
    }

    [Fact]
    public void Every_word_is_in_the_markup()
    {
        // The animation is CSS, so a reader who never sees it still reads the list — which is also why
        // the surrounding phrase has to make sense with every word in it.
        var html = UiTextRotate.Words(["fast", "typed", "small"]).ToHtml();

        Assert.Contains("fast", html);
        Assert.Contains("typed", html);
        Assert.Contains("small", html);
    }

    [Fact]
    public void The_rotator_is_defined_in_the_shipped_sheet() =>
        Assert.Contains(".text-rotate", UiStylesheet.Css, StringComparison.Ordinal);

    [Fact]
    public void The_rotator_takes_its_speed_from_a_call_site_class()
    {
        // There is no Interval property on purpose: daisyUI reads the cycle from --tw-duration, which a
        // `duration-*` utility sets, and turning a TimeSpan into a class name at run time would produce
        // a name Tailwind never scanned and the sheet never defined.
        Assert.Contains("duration-[3s]", UiTextRotate.Words(["a", "b"]).Class("duration-[3s]").ToHtml());
    }
}
