using Rask.Core;
using Rask.Web.Types;

namespace Rask.Web.Tests;

// A test's stand-in for a web object: every chain that starts at it is answered by the fake, and nothing reaches the
// browser — here there is none at all, and a call outside a page would throw.
public sealed class WebFakeTests
{
    [Fact]
    public async Task A_faked_member_answers_what_the_test_set_up()
    {
        using var clipboard = Navigator.Clipboard.Fake();
        clipboard.Returns(c => c.ReadText(), "pasted");

        var text = await Navigator.Clipboard.ReadText();

        Assert.Equal("pasted", text);
    }

    [Fact]
    public async Task A_call_on_a_faked_object_is_recorded_under_MDNs_name()
    {
        using var clipboard = Navigator.Clipboard.Fake();

        await Navigator.Clipboard.WriteText("hi");

        var call = Assert.Single(clipboard.Calls);
        Assert.Equal(("writeText", "hi"), (call.Member, (string)call.Args[0]!));
    }

    [Fact]
    public async Task A_global_is_faked_the_same_way()
    {
        using var storage = LocalStorage.Fake();
        storage.Returns(s => s.GetItem("theme"), "dark");

        await LocalStorage.SetItem("theme", "light");
        var theme = await LocalStorage.GetItem("theme");

        Assert.Equal("dark", theme);
        Assert.Equal(["setItem", "getItem"], storage.Calls.Select(c => c.Member));
    }

    [Fact]
    public async Task A_write_to_a_faked_object_is_read_back()
    {
        using var window = Window.Fake();

        await Window.SetName("checkout");
        var name = await Window.Name;

        Assert.Equal("checkout", name);
        Assert.Equal("name=", window.Calls.Single().Member);
    }

    [Fact]
    public async Task An_object_kept_from_a_fake_stays_in_it()
    {
        using var query = Window.MatchMedia("(min-width: 900px)").Fake();
        query.Returns(q => q.Matches, true);

        await using var kept = await Window.MatchMedia("(min-width: 900px)");
        var matches = await kept.Matches;

        Assert.True(matches);
    }

    [Fact]
    public async Task A_raised_event_reaches_the_handler_subscribed_in_a_component()
    {
        using var query = Window.MatchMedia("(min-width: 900px)").Fake();
        var widget = new Widget();
        await using var subscription = await widget.WatchWidth();

        query.Raise("change", new MediaQueryListEvent { Media = "(min-width: 900px)", Matches = true });

        Assert.True(widget.Wide);
    }

    [Fact]
    public async Task A_member_a_fake_throws_for_fails_as_the_browser_refusing_it_would()
    {
        using var storage = Navigator.Storage.Fake();
        storage.Throws(s => s.Persist(), new Microsoft.JSInterop.JSException("denied"));

        var error = await Assert.ThrowsAsync<Microsoft.JSInterop.JSException>(async () => await Navigator.Storage.Persist());

        Assert.Equal(("denied", "persist"), (error.Message, storage.Calls.Single().Member));
    }

    [Fact]
    public async Task A_disposed_fake_hands_the_object_back_to_the_browser()
    {
        var clipboard = Navigator.Clipboard.Fake();

        clipboard.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Navigator.Clipboard.ReadText());
    }

    private sealed class Widget : Component
    {
        public bool Wide { get; private set; }

        public ValueTask<IAsyncDisposable> WatchWidth() => Window.MatchMedia("(min-width: 900px)").OnChange(e => Wide = e.Matches);
    }
}
