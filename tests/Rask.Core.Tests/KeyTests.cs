using System.Text;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests;

// Component-level Key (Blazor @key parity): emits data-rask-key on an element, and is
// auto-forwarded onto the first rendered element of a transparent component / Fragment.
public partial class KeyTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Key_OnElement_EmitsDataRaskKeyInDataGroup()
    {
        // Order is id, class, style, data-*, then data-rask-key (still inside the data-* run).
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-rask-key=\"k1\"></div>",
            Div.Id("i").Class("c").Style("s").Key("k1").ToHtml());
    }

    [Fact]
    public void Key_NonStringValue_StringifiedOnEmit() =>
        Assert.Equal("<li data-rask-key=\"42\"></li>", Li.Key(42).ToHtml());

    [Fact]
    public void Key_Null_EmitsNothing() => Assert.Equal("<div></div>", Div.ToHtml());

    [Fact]
    public void Key_ValueKey_ReEmitsStablyAcrossRenders()
    {
        // KeyString dropped its value→string cache (a footprint win); a boxed value key must still
        // stringify correctly on every render, not just the first.
        var li = Li.Key(42);
        Assert.Equal("<li data-rask-key=\"42\"></li>", li.ToHtml());
        Assert.Equal("<li data-rask-key=\"42\"></li>", li.ToHtml());

        // Reading Key back returns the original boxed value unchanged.
        Assert.Equal(42, li.Value.Key);
    }

    [Fact]
    public void Key_AfterUserData_NoDuplicate()
    {
        // data-rask-key follows other data-* entries; a literal Data["rask-key"] is dropped
        // in favour of the canonical Key so there's exactly one data-rask-key.
        Assert.Equal(
            "<li data-row=\"7\" data-rask-key=\"k\"></li>",
            Li.Data(new Dictionary<string, string?> { ["row"] = "7", ["rask-key"] = "from-data" }).Key("k")
                .ToHtml());
    }

    [Fact]
    public void DataRaskKey_WithoutKeyProp_StillEmits_BackCompat()
    {
        // VirtualizePage-style keying via Data continues to work when Key isn't set.
        Assert.Equal(
            "<tr data-rask-key=\"3\"></tr>",
            Tr.Data(new Dictionary<string, string?> { ["rask-key"] = "3" }).ToHtml());
    }

    [Fact]
    public void Key_OnFragment_ForwardsToFirstElementOnly()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Fragment.Key("k1")[Div.Class("line")[Text.Value("x")], Div[Text.Value("y")]], sb);
        Assert.Equal("<div class=\"line\" data-rask-key=\"k1\">x</div><div>y</div>", sb.ToString());
    }

    [Fact]
    public void Key_OnTransparentComponent_ForwardsToRootElement()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new KeyWrapper(Tr[Td["cell"]]) { Key = "row-7" }, sb);
        Assert.Equal("<tr data-rask-key=\"row-7\"><td>cell</td></tr>", sb.ToString());
    }

    [Fact]
    public void Key_ElementOwnKey_WinsOverForwarded()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new KeyWrapper(Div.Key("inner")[Text.Value("x")]) { Key = "outer" }, sb);
        Assert.Equal("<div data-rask-key=\"inner\">x</div>", sb.ToString());
    }

    [Fact]
    public void Key_OnComponentRenderingNoElement_DoesNotLeakToSibling()
    {
        // The inner keyed Fragment renders only text — its key must NOT spill onto the
        // following sibling Div (the slot is cleared after the keyed body serializes).
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(
            [Fragment.Key("k")[Text.Value("t")], Div[Text.Value("x")]], sb);
        Assert.Equal("t<div>x</div>", sb.ToString());
    }

    private sealed class KeyWrapper : Component
    {
        private readonly Component _body;
        public KeyWrapper(Component body) => _body = body;
        protected override Component? Render() => _body;
    }
}
