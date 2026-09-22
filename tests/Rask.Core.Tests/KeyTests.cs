using System.Text;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests;

// Component-level Key (Blazor @key parity): emits data-rask-key on an element, and is
// auto-forwarded onto the first rendered element of a transparent component / Fragment.
public partial class KeyTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_key_on_an_element_emits_data_rask_key_in_the_data_group()
    {
        // Order is id, class, style, data-*, then data-rask-key (still inside the data-* run).
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-rask-key=\"k1\"></div>",
            Div.Id("i").Class("c").Style("s").Key("k1").ToHtml());
    }

    [Fact]
    public void A_non_string_key_is_stringified_on_emit() =>
        Assert.Equal("<li data-rask-key=\"42\"></li>", Li.Key(42).ToHtml());

    [Fact]
    public void A_null_key_emits_nothing() => Assert.Equal("<div></div>", Div.ToHtml());

    [Fact]
    public void A_value_key_emits_stably_across_renders()
    {
        // KeyString dropped its value→string cache (a footprint win); a boxed value key must still
        // stringify correctly on every render, not just the first.
        var li = Li.Key(42);
        Assert.Equal("<li data-rask-key=\"42\"></li>", li.ToHtml());
        Assert.Equal("<li data-rask-key=\"42\"></li>", li.ToHtml());

        // Reading Key back returns the original boxed value unchanged.
        Assert.Equal(42, li.Key);
    }

    [Fact]
    public void A_key_after_user_data_is_not_duplicated()
    {
        // data-rask-key follows other data-* entries; a literal Data["rask-key"] is dropped
        // in favour of the canonical Key so there's exactly one data-rask-key.
        Assert.Equal(
            "<li data-row=\"7\" data-rask-key=\"k\"></li>",
            Li.Data(new Dictionary<string, string?> { ["row"] = "7", ["rask-key"] = "from-data" }).Key("k")
                .ToHtml());
    }

    [Fact]
    public void A_data_rask_key_without_the_Key_prop_still_emits_for_back_compat()
    {
        // VirtualizePage-style keying via Data continues to work when Key isn't set.
        Assert.Equal(
            "<tr data-rask-key=\"3\"></tr>",
            Tr.Data(new Dictionary<string, string?> { ["rask-key"] = "3" }).ToHtml());
    }

    [Fact]
    public void A_key_on_a_fragment_forwards_to_the_first_element_only()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Fragment.Key("k1")[Div.Class("line")[Text.Value("x")], Div[Text.Value("y")]], sb);

        Assert.Equal("<div class=\"line\" data-rask-key=\"k1\">x</div><div>y</div>", sb.ToString());
    }

    [Fact]
    public void A_key_on_a_transparent_component_forwards_to_its_root_element()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new KeyWrapper(Tr[Td["cell"]]) { Key = "row-7" }, sb);

        Assert.Equal("<tr data-rask-key=\"row-7\"><td>cell</td></tr>", sb.ToString());
    }

    [Fact]
    public void An_elements_own_key_wins_over_a_forwarded_one()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new KeyWrapper(Div.Key("inner")[Text.Value("x")]) { Key = "outer" }, sb);

        Assert.Equal("<div data-rask-key=\"inner\">x</div>", sb.ToString());
    }

    [Fact]
    public void A_key_on_a_component_rendering_no_element_does_not_leak_to_a_sibling()
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
