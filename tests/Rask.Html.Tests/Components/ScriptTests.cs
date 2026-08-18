namespace Rask.Html.Tests.Components;

public partial class ScriptTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<script></script>", Script.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<script id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/app.js\" type=\"module\" async defer crossorigin=\"anonymous\" integrity=\"sha384-abc\" nomodule referrerpolicy=\"no-referrer\" charset=\"utf-8\"></script>",
            Script
                .Src("/app.js")
                .Type("module")
                .Async(true)
                .Defer(true)
                .CrossOrigin("anonymous")
                .Integrity("sha384-abc")
                .NoModule(true)
                .ReferrerPolicy("no-referrer")
                .Charset("utf-8")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<script>&lt;x&gt;</script>", Script["<x>"].ToHtml());

    [Fact]
    public void Render_FetchPriorityAndBlocking_EmitAfterTheOtherScriptAttrs() =>
        // `blocking="render"` is an opt-IN to blocking, which is the reverse of every other loading
        // knob on this element.
        Assert.Equal(
            "<script src=\"/a.js\" fetchpriority=\"low\" blocking=\"render\"></script>",
            Script.Src("/a.js").FetchPriority("low").Blocking("render").ToHtml());
}
