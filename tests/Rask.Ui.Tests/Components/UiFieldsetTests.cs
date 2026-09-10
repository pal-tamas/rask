namespace Rask.Ui.Tests.Components;

/// <summary>
///     The control group, and the caption it is required to render.
/// </summary>
public partial class UiFieldsetTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_caption_is_the_legend()
    {
        // It rendered `Title` — the <title> TAG's chain entry, inherited from Component — instead of its
        // own required Text. So the one property the component makes you pass never reached the page, and
        // the legend carried a stray <title> element instead. The class name and the <fieldset>/<legend>
        // structure were all correct, which is why nothing looked wrong enough to chase.
        var html = UiFieldset.Text("Email")[Span["field"]].ToHtml();

        Assert.Contains("<legend class=\"fieldset-legend\">Email</legend>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void It_never_renders_a_title_element() =>
        Assert.DoesNotContain(
            "<title",
            UiFieldset.Text("Email")[Span["field"]].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void It_is_a_real_fieldset_with_a_real_legend()
    {
        // Not a styled div: this is what gives a screen reader the caption-to-controls relationship and
        // what lets a browser disable the whole group at once.
        var html = UiFieldset.Text("Email")[Span["field"]].ToHtml();

        Assert.Contains("<fieldset", html, StringComparison.Ordinal);
        Assert.Contains("<legend", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_text_is_rendered_when_given() =>
        Assert.Contains(
            "We never share it.",
            UiFieldset.Text("Email").Help("We never share it.")[Span["field"]].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void An_own_class_joins_the_daisyui_one() =>
        Assert.Contains(
            "fieldset w-full",
            UiFieldset.Text("Email").Class("w-full")[Span["field"]].ToHtml(),
            StringComparison.Ordinal);
}
