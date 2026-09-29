using Rask.Web.Types;

namespace Rask.Web.Tests;

// MDN's globals, generated from MDN's data: a chain of reads and calls, run in the browser in one round trip when it is
// awaited, its steps under MDN's names.
public sealed class WebApiTests
{
    [Fact]
    public async Task A_chain_from_a_global_runs_in_one_round_trip_under_MDNs_names()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await Navigator.Clipboard.WriteText("hi");
        }

        Assert.Equal(["__raskWeb.run"], browser.Calls.Select(c => c.Identifier));
        Assert.Equal("""[["g","navigator"],["g","clipboard"],["c","writeText",["hi"]]]""", browser.Steps(0));
    }

    [Fact]
    public void Nothing_crosses_to_the_browser_until_a_member_is_awaited()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            _ = Window.MatchMedia("(prefers-color-scheme: dark)");
            _ = Navigator.Clipboard;
        }

        Assert.Empty(browser.Calls);
    }

    [Fact]
    public async Task A_value_is_read_from_the_end_of_the_chain()
    {
        var browser = new FakeBrowser().Answers("true");

        bool dark;
        using (browser.Enter())
        {
            dark = await Window.MatchMedia("(prefers-color-scheme: dark)").Matches;
        }

        Assert.True(dark);
        Assert.Equal("""[["c","matchMedia",["(prefers-color-scheme: dark)"]],["g","matches"]]""", browser.Steps(0));
    }

    [Fact]
    public async Task An_awaited_object_is_kept_and_its_members_start_from_the_handle()
    {
        var browser = new FakeBrowser().Answers("false");

        bool wide;
        using (browser.Enter())
        {
            await using var query = await Window.MatchMedia("(min-width: 800px)");
            wide = await query.Matches;
        }

        Assert.False(wide);
        Assert.Same(browser.Kept.Single(), browser.Calls[1].Args[0]);
        Assert.Equal("""[["g","matches"]]""", browser.Steps(1));
        Assert.True(browser.Kept.Single().Disposed);
    }

    [Fact]
    public async Task Options_and_enums_cross_in_MDNs_own_shape()
    {
        var browser = new FakeBrowser().Answers("\"hidden\"");

        DocumentVisibilityState state;
        using (browser.Enter())
        {
            await Navigator.Share(new ShareData { Title = "Rask", Url = "https://rask.sh" });
            state = await Document.VisibilityState;
        }

        Assert.Equal("""[["g","navigator"],["c","share",[{"title":"Rask","url":"https://rask.sh"}]]]""", browser.Steps(0));
        Assert.Equal(DocumentVisibilityState.Hidden, state);
    }

    [Fact]
    public async Task Writing_a_property_is_a_setter_named_for_it()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await Window.SetName("checkout");
        }

        Assert.Equal("""[["s","name",["checkout"]]]""", browser.Steps(0));
    }

    [Fact]
    public void What_the_page_renders_has_no_setter()
    {
        var document = typeof(JsObject).Assembly.GetType("Rask.Web.Document")!;

        var members = document.GetMethods().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal((true, false), (members.Contains("get_Title"), members.Contains("SetTitle")));
    }

    [Fact]
    public async Task Is_supported_asks_the_browser_whether_the_object_is_there()
    {
        var browser = new FakeBrowser().Answers("true");

        bool supported;
        using (browser.Enter())
        {
            supported = await Navigator.Clipboard.IsSupported;
        }

        Assert.True(supported);
        Assert.Equal(("__raskWeb.has", """[["g","navigator"],["g","clipboard"]]"""), (browser.Calls[0].Identifier, browser.Steps(0)));
    }

    [Fact]
    public async Task A_web_API_called_outside_a_page_says_where_to_call_it_from()
    {
        var call = Navigator.Clipboard;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await call.WriteText("hi"));

        Assert.Contains("from an event handler or from OnRendered", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_DOM_stays_Rasks_so_no_node_is_a_web_object()
    {
        var types = typeof(JsObject).Assembly.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var nodes = new[] { "Node", "Element", "HTMLElement", "Text", "DocumentFragment" }.Where(types.Contains).ToList();

        Assert.Equal([], nodes);
        Assert.Contains("Document", types);
    }
}
