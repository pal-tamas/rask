using System.Text.Json;

namespace Rask.Core.Tests.Interop;

public partial class ElementRefTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_new_ref_generates_unique_selector_safe_ids()
    {
        var a = ElementRef.New();
        var b = ElementRef.New();

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEmpty(a.Id);
        // GUID "N" format: 32 hex chars, always safe inside an attribute selector.
        Assert.Matches("^[0-9a-f]{32}$", a.Id);
    }

    [Fact]
    public void Serializing_a_ref_emits_the_rask_ref_marker()
    {
        var r = ElementRef.New();

        var json = JsonSerializer.Serialize(r);

        Assert.Equal($"{{\"__raskRef__\":\"{r.Id}\"}}", json);
    }

    [Fact]
    public void An_element_with_a_ref_emits_data_rask_ref()
    {
        var r = ElementRef.New();

        var html = Div.Ref(r)["body"].ToHtml();

        Assert.Contains($"data-rask-ref=\"{r.Id}\"", html);
    }

    [Fact]
    public void An_element_without_a_ref_emits_no_data_rask_ref()
    {
        var html = Div["body"].ToHtml();

        Assert.DoesNotContain("data-rask-ref", html);
    }

    [Fact]
    public void The_ref_sits_in_the_data_group_after_style_and_before_the_tag_specifics()
    {
        var r = ElementRef.New();
        // Anchor (A) has a tag-specific href; assert id/class/style/data-* (incl. rask-ref) all
        // precede it — the documented attribute order: id, class, style, data-*, tag-specific.
        var html = A.Id("x").Class("c").Style("color:red").Href("/go").Ref(r)["link"].ToHtml();

        var idIdx = html.IndexOf("id=\"x\"", StringComparison.Ordinal);
        var classIdx = html.IndexOf("class=\"c\"", StringComparison.Ordinal);
        var styleIdx = html.IndexOf("style=\"color:red\"", StringComparison.Ordinal);
        var refIdx = html.IndexOf("data-rask-ref=", StringComparison.Ordinal);
        var hrefIdx = html.IndexOf("href=\"/go\"", StringComparison.Ordinal);

        Assert.True(idIdx < classIdx);
        Assert.True(classIdx < styleIdx);
        Assert.True(styleIdx < refIdx);
        Assert.True(refIdx < hrefIdx, "data-rask-ref (data-* group) must precede tag-specific href");
    }
}
