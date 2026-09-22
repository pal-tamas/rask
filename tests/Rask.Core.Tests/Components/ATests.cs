#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class ATests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() => Assert.Equal("<a></a>", A.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<a id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/foo\" target=\"_blank\" rel=\"noopener\" download=\"file.zip\" hreflang=\"en\" type=\"text/html\" referrerpolicy=\"no-referrer\" ping=\"https://ping\"></a>",
            A
                .Href("/foo")
                .Target("_blank")
                .Rel("noopener")
                .Download("file.zip")
                .Hreflang("en")
                .Type("text/html")
                .ReferrerPolicy("no-referrer")
                .Ping("https://ping")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() => Assert.Equal("<a>&lt;x&gt;</a>", A["<x>"].ToHtml());

    [Fact]
    public void OnClick_outside_a_live_context_emits_no_handler_attribute() =>
        Assert.Equal("<a></a>", A.OnClick(() => { }).ToHtml());

    [Fact]
    public void OnClick_inside_a_live_context_emits_the_click_handler_id()
    {
        var view = new StubComponent(() => A.OnClick(() => { })["go"]);

        Assert.Equal("<a data-rask-on-click=\"h0\">go</a>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void An_async_OnClick_inside_a_live_context_emits_the_click_handler_id()
    {
        var view = new StubComponent(() => A.OnClick(async () => { await Task.Yield(); })["go"]);

        Assert.Equal("<a data-rask-on-click=\"h0\">go</a>", view.RenderAsLiveRoot());
    }
}
