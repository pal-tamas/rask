using System.Text.Json;

namespace Rask.Core.Tests.Interop;

public class ElementRefTests
{
    [Fact]
    public void New_GeneratesUnique_SelectorSafeIds()
    {
        var a = ElementRef.New();
        var b = ElementRef.New();

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEmpty(a.Id);
        // GUID "N" format: 32 hex chars, always safe inside an attribute selector.
        Assert.Matches("^[0-9a-f]{32}$", a.Id);
    }

    [Fact]
    public void Serialize_EmitsRaskRefMarker()
    {
        var r = ElementRef.New();

        var json = JsonSerializer.Serialize(r);

        Assert.Equal($"{{\"__raskRef__\":\"{r.Id}\"}}", json);
    }

    [Fact]
    public void Element_WithRef_EmitsDataRaskRef()
    {
        var r = ElementRef.New();

        var html = Div(Ref: r)["body"].ToHtml();

        Assert.Contains($"data-rask-ref=\"{r.Id}\"", html);
    }

    [Fact]
    public void Element_WithoutRef_EmitsNoDataRaskRef()
    {
        var html = Div()["body"].ToHtml();

        Assert.DoesNotContain("data-rask-ref", html);
    }

    [Fact]
    public void Ref_SitsInDataGroup_AfterStyle_BeforeTagSpecifics()
    {
        var r = ElementRef.New();
        // Anchor (A) has a tag-specific href; assert id/class/style/data-* (incl. rask-ref) all
        // precede it — the documented attribute order: id, class, style, data-*, tag-specific.
        var html = A(Id: "x", Class: "c", Style: "color:red", Href: "/go", Ref: r)["link"].ToHtml();

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
