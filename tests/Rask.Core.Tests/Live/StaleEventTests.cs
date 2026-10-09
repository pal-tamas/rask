using System.Text.Json;
using System.Text.RegularExpressions;
using Entries = RaskEntriesRask_Core_Tests;

#pragma warning disable RASK014 // test-defined component subclasses have no generated factories
#pragma warning disable RASK022 // one test is about rows that carry no key

namespace Rask.Core.Tests.Live;

/// <summary>
///     An event names the page it was read from (<c>v</c>). When the page has moved on since, the event reaches
///     the handler it was sent to, wherever that handler is now — or nothing. Never another handler.
/// </summary>
public partial class StaleEventTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(int Id, string Name);

    private static JsonElement Event(string id, int? version, string type = "click", string? value = null)
    {
        var v = version is null ? "" : $",\"v\":{version}";
        var val = value is null ? "" : $",\"value\":\"{value}\"";
        return JsonDocument.Parse($$"""{"id":"{{id}}","type":"{{type}}"{{val}}{{v}}}""").RootElement;
    }

    private static ValueTask<bool> Send(Component root, string id, int? version, string type = "click", string? value = null) =>
        root.TryInvokeHandlerAsync(id, Event(id, version, type, value));

    private static string IdOn(string html, string cls, string on = "click")
    {
        var m = Regex.Match(html, $"class=\"{cls}\"[^>]*?data-rask-on-{on}=\"([^\"]+)\"");
        Assert.True(m.Success, $"no {on} hook on .{cls} in: {html}");
        return m.Groups[1].Value;
    }

    // Renders, and says the browser was sent the result.
    private static string Ship(Component root)
    {
        var html = root.RenderAsLiveRoot();
        root.HandlersSent();
        return html;
    }

    [Fact]
    public async Task A_click_on_a_button_that_has_left_runs_nothing()
    {
        var show = true;
        var cancel = 0;
        var delete = 0;
        var view = new StubComponent(() => Div[
            show ? Button.Class("cancel").OnClick(() => cancel++)["Cancel"] : null,
            Button.Class("delete").OnClick(() => delete++)["Delete"]]);
        var cancelId = IdOn(Ship(view), "cancel");
        show = false;
        Ship(view);

        var ran = await Send(view, cancelId, version: 0);

        Assert.False(ran);
        Assert.Equal((0, 0), (cancel, delete));
    }

    [Fact]
    public async Task A_click_on_a_keyed_button_that_has_left_runs_nothing()
    {
        var show = true;
        var cancel = 0;
        var delete = 0;
        var view = new StubComponent(() => Div[
            show ? Button.Key("c").Class("cancel").OnClick(() => cancel++)["Cancel"] : null,
            Button.Key("d").Class("delete").OnClick(() => delete++)["Delete"]]);
        var cancelId = IdOn(Ship(view), "cancel");
        show = false;
        Ship(view);

        var ran = await Send(view, cancelId, version: 0);

        Assert.False(ran);
        Assert.Equal((0, 0), (cancel, delete));
    }

    [Fact]
    public async Task A_click_sent_before_an_earlier_button_left_still_reaches_its_own_button()
    {
        var show = true;
        var cancel = 0;
        var delete = 0;
        var view = new StubComponent(() => Div[
            show ? Button.Class("cancel").OnClick(() => cancel++)["Cancel"] : null,
            Button.Class("delete").OnClick(() => delete++)["Delete"]]);
        var deleteId = IdOn(Ship(view), "delete");
        show = false;
        Ship(view);

        var ran = await Send(view, deleteId, version: 0);

        Assert.True(ran);
        Assert.Equal((0, 1), (cancel, delete));
    }

    [Fact]
    public async Task A_click_sent_before_an_earlier_button_arrived_still_reaches_its_own_button()
    {
        var show = false;
        var cancel = 0;
        var delete = 0;
        var view = new StubComponent(() => Div[
            show ? Button.Class("cancel").OnClick(() => cancel++)["Cancel"] : null,
            Button.Class("delete").OnClick(() => delete++)["Delete"]]);
        var deleteId = IdOn(Ship(view), "delete");
        show = true;
        Ship(view);

        var ran = await Send(view, deleteId, version: 0);

        Assert.True(ran);
        Assert.Equal((0, 1), (cancel, delete));
    }

    // The production shape: a row per record with a bin, then a confirmation that is always there.
    private sealed class Ledger : Component
    {
        public List<Row> Rows = [new(1, "a"), new(2, "b"), new(3, "c")];
        public Row? Removing;
        public List<int> Removed = [];
        public int Closed;

        protected override Component? Render() => Div[
            Rows.Select(row => Div.Key(row.Id)[
                A.Class($"edit{row.Id}").Href($"/rows/{row.Id}")["edit"],
                Button.Class($"bin{row.Id}").OnClick(() => Removing = row)["bin"]]),
            Div.Key("confirm-removal")[
                Button.Class("no").OnClick(() => { Removing = null; Closed++; })["No"],
                Button.Class("yes").OnClick(Remove)["Yes"]]];

        private void Remove()
        {
            if (Removing is null)
            {
                return;
            }

            Removed.Add(Removing.Id);
            Rows = Rows.Where(row => row.Id != Removing.Id).ToList();
            Removing = null;
        }
    }

    [Fact]
    public async Task A_click_on_the_last_row_s_bin_sent_before_a_row_was_removed_asks_about_that_row_only()
    {
        var ledger = new Ledger();
        await Send(ledger, IdOn(Ship(ledger), "bin1"), version: 0);
        var asking = Ship(ledger);
        await Send(ledger, IdOn(asking, "yes"), version: 1);
        Ship(ledger);

        await Send(ledger, IdOn(asking, "bin3"), version: 1);

        Assert.Equal([1], ledger.Removed);
        Assert.Equal(3, ledger.Removing?.Id);
        Assert.Equal(0, ledger.Closed);
    }

    [Fact]
    public async Task A_second_click_on_the_bin_of_a_row_that_was_removed_runs_nothing()
    {
        var ledger = new Ledger();
        await Send(ledger, IdOn(Ship(ledger), "bin1"), version: 0);
        var asking = Ship(ledger);
        await Send(ledger, IdOn(asking, "yes"), version: 1);
        Ship(ledger);

        var ran = await Send(ledger, IdOn(asking, "bin1"), version: 1);

        Assert.False(ran);
        Assert.Null(ledger.Removing);
        Assert.Equal([1], ledger.Removed);
    }

    [Fact]
    public async Task A_click_on_the_confirmation_sent_before_a_row_was_removed_still_confirms()
    {
        var ledger = new Ledger();
        var before = Ship(ledger);
        ledger.Rows = ledger.Rows.Skip(1).ToList();
        ledger.Removing = ledger.Rows[0];
        Ship(ledger);

        await Send(ledger, IdOn(before, "yes"), version: 0);

        Assert.Equal([2], ledger.Removed);
    }

    private sealed class Pair(string name) : Component
    {
        public bool Extra;
        public int Extras;
        public int Clicks;

        protected override Component? Render() => Div[
            Extra ? Button.Class($"{name}-extra").OnClick(() => Extras++)["extra"] : null,
            Button.Class(name).OnClick(() => Clicks++)[name]];
    }

    [Fact]
    public async Task A_button_arriving_in_one_component_leaves_a_click_on_another_component_alone()
    {
        var first = new Pair("first");
        var second = new Pair("second");
        var view = new StubComponent(() => Div[first, second]);
        var secondId = IdOn(Ship(view), "second");
        first.Extra = true;
        first.StateHasChanged();
        Ship(view);

        var ran = await Send(view, secondId, version: 0);

        Assert.True(ran);
        Assert.Equal((0, 0, 1), (first.Extras, first.Clicks, second.Clicks));
    }

    [Fact]
    public async Task A_click_reaches_its_handler_however_many_times_the_page_was_sent_since()
    {
        var count = 0;
        var label = 0;
        var view = new StubComponent(() => Div[Button.Class("go").OnClick(() => count++)[$"go {label}"]]);
        var id = IdOn(Ship(view), "go");
        for (var i = 0; i < 5; i++)
        {
            label++;
            Ship(view);
        }

        var ran = await Send(view, id, version: 0);

        Assert.True(ran);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_click_on_a_component_that_was_unmounted_runs_nothing()
    {
        var show = true;
        var leaf = new Pair("leaf");
        var other = new Pair("other");
        var view = new StubComponent(() => Div[show ? leaf : null, other]);
        var leafId = IdOn(Ship(view), "leaf");
        show = false;
        Ship(view);

        var ran = await Send(view, leafId, version: 0);

        Assert.False(ran);
        Assert.Equal((0, 0), (leaf.Clicks, other.Clicks));
    }

    [Fact]
    public async Task A_render_that_sent_nothing_does_not_age_the_page()
    {
        var row = new Row(1, "a");
        Row? picked = null;
        var view = new StubComponent(() =>
        {
            var captured = row;
            return Div[Button.Class("pick").OnClick(() => picked = captured)["pick"]];
        });
        var id = IdOn(Ship(view), "pick");
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

        var ran = await Send(view, id, version: 0);

        Assert.True(ran);
        Assert.Same(row, picked);
    }

    [Fact]
    public async Task A_burst_typed_into_one_field_reaches_it_every_time()
    {
        var row = new Row(1, "a");
        var typed = new List<string>();
        var view = new StubComponent(() =>
        {
            var captured = row;
            return Div[Input.Value(typed.LastOrDefault() ?? "").Class("name").OnInput(v => typed.Add(captured.Name + v))];
        });
        var id = IdOn(Ship(view), "name", "input");

        foreach (var text in new[] { "x", "xy", "xyz", "xyzw" })
        {
            await Send(view, id, version: 0, "input", text);
            Ship(view);
        }

        Assert.Equal(["ax", "axy", "axyz", "axyzw"], typed);
    }

    [Fact]
    public async Task A_field_left_after_a_burst_was_typed_into_it_still_hears_the_change()
    {
        var row = new Row(1, "a");
        var typed = new List<string>();
        var changed = new List<string>();
        var view = new StubComponent(() =>
        {
            var captured = row;
            return Div[
                Input.Value(typed.LastOrDefault() ?? "").Class("name").OnInput(v => typed.Add(captured.Name + v)),
                Button.Class("save").OnClick(() => changed.Add(captured.Name))["save"]];
        });
        var html = Ship(view);
        await Send(view, IdOn(html, "name", "input"), version: 0, "input", "x");
        Ship(view);
        await Send(view, IdOn(html, "name", "input"), version: 0, "input", "xy");
        Ship(view);

        var ran = await Send(view, IdOn(html, "save"), version: 0);

        Assert.True(ran);
        Assert.Equal(["a"], changed);
    }

    [Fact]
    public async Task A_click_on_a_row_whose_record_was_replaced_by_an_unequal_one_runs_nothing()
    {
        var rows = new List<object> { new(), new() };
        object? picked = null;
        var view = new StubComponent(() => Div[rows.Select((row, i) => Button.Class($"row{i}").OnClick(() => picked = row)["pick"])]);
        var id = IdOn(Ship(view), "row1");
        rows = [new(), new()];
        Ship(view);

        var ran = await Send(view, id, version: 0);

        Assert.False(ran);
        Assert.Null(picked);
    }

    [Fact]
    public async Task An_event_that_names_no_page_is_read_against_the_page_as_it_is_now()
    {
        var show = true;
        var cancel = 0;
        var delete = 0;
        var view = new StubComponent(() => Div[
            show ? Button.Class("cancel").OnClick(() => cancel++)["Cancel"] : null,
            Button.Class("delete").OnClick(() => delete++)["Delete"]]);
        Ship(view);
        show = false;
        var deleteId = IdOn(Ship(view), "delete");

        var ran = await Send(view, deleteId, version: null);

        Assert.True(ran);
        Assert.Equal((0, 1), (cancel, delete));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public async Task An_event_from_the_page_as_it_is_now_or_a_later_one_reaches_the_handler_there(int version)
    {
        var show = true;
        var cancel = 0;
        var delete = 0;
        var view = new StubComponent(() => Div[
            show ? Button.Class("cancel").OnClick(() => cancel++)["Cancel"] : null,
            Button.Class("delete").OnClick(() => delete++)["Delete"]]);
        Ship(view);
        show = false;
        var deleteId = IdOn(Ship(view), "delete");

        var ran = await Send(view, deleteId, version);

        Assert.True(ran);
        Assert.Equal((0, 1), (cancel, delete));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"0\"")]
    [InlineData("99999999999999999999")]
    public async Task An_event_whose_page_is_not_a_whole_number_runs_nothing(string version)
    {
        var count = 0;
        var view = new StubComponent(() => Div[Button.Class("go").OnClick(() => count++)["go"]]);
        var id = IdOn(Ship(view), "go");
        var payload = JsonDocument.Parse($$"""{"id":"{{id}}","type":"click","v":{{version}}}""").RootElement;

        var ran = await view.TryInvokeHandlerAsync(id, payload);

        Assert.False(ran);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task A_page_delivered_as_a_document_is_the_page_an_event_without_history_was_read_from()
    {
        var loaded = false;
        var cancel = 0;
        var open = 0;
        var view = new StubComponent(() => Div[
            loaded ? Button.Class("open").OnClick(() => open++)["Open"] : Button.Class("cancel").OnClick(() => cancel++)["Cancel"]]);
        view.RenderAsLiveRoot();
        loaded = true;
        var html = view.RenderAsLiveRoot();
        view.HandlersDelivered();

        var ran = await Send(view, IdOn(html, "open"), version: 0);

        Assert.True(ran);
        Assert.Equal((0, 1), (cancel, open));
    }

    [Fact]
    public void A_page_whose_handlers_moved_is_numbered_when_it_is_sent_and_one_whose_handlers_stayed_is_not()
    {
        var show = true;
        var label = 0;
        var view = new StubComponent(() => Div[
            show ? Button.OnClick(Noop)["Cancel"] : null,
            Button.OnClick(Other)[$"Delete {label}"]]);
        Ship(view);
        label++;
        view.RenderAsLiveRoot();
        var stayed = view.HandlerVersionToSend(whole: false);
        view.HandlersSent();
        show = false;
        view.RenderAsLiveRoot();

        var moved = view.HandlerVersionToSend(whole: false);

        Assert.Null(stayed);
        Assert.Equal(1, moved);
    }

    [Fact]
    public void A_whole_page_always_says_which_page_it_is()
    {
        var view = new StubComponent(() => Div[Button.OnClick(Noop)["go"]]);
        view.RenderAsLiveRoot();

        var version = view.HandlerVersionToSend(whole: true);

        Assert.Equal(0, version);
    }

    // PINNED, NOT WANTED. Child components without a key are the same instances by position, so the second
    // row's component is the second row's component whatever record it is handed: its handler is the same method
    // of the same instance, and nothing about the handler moved. The event reaches the row that now sits second.
    // Keying the rows (`.Key(row.Id)`) is what ties an instance to a record, and then this click runs nothing.
    [Fact]
    public async Task A_click_on_an_unkeyed_row_component_reaches_whichever_row_sits_in_its_place_now()
    {
        global::Rask.Core.Tests.Live.StaleRow.Picked.Clear();
        var ids = new List<int> { 1, 2, 3 };
        var view = new StubComponent(() => Div[ids.Select(id => (Component)Entries.StaleRow.Id(id))]);
        var second = IdOn(Ship(view), "pick2");
        ids.RemoveAt(0);
        Ship(view);

        var ran = await Send(view, second, version: 0);

        Assert.True(ran);
        Assert.Equal([3], global::Rask.Core.Tests.Live.StaleRow.Picked);
    }

    [Fact]
    public async Task A_click_on_a_keyed_row_component_whose_row_moved_up_reaches_that_row()
    {
        global::Rask.Core.Tests.Live.StaleRow.Picked.Clear();
        var ids = new List<int> { 1, 2, 3 };
        var view = new StubComponent(() => Div[ids.Select(id => (Component)Entries.StaleRow.Id(id).Key(id))]);
        var second = IdOn(Ship(view), "pick2");
        ids.RemoveAt(0);
        Ship(view);

        var ran = await Send(view, second, version: 0);

        Assert.True(ran);
        Assert.Equal([2], global::Rask.Core.Tests.Live.StaleRow.Picked);
    }

    private static void Noop()
    {
    }

    private static void Other()
    {
    }
}

/// <summary>A row that is a component of its own: its handler is a method of the instance.</summary>
public sealed partial class StaleRow : Component
{
    internal static readonly List<int> Picked = [];

    public required int Id { get; set; }

    protected override Component? Render() => Button.Class($"pick{Id}").OnClick(Pick)["pick"];

    private void Pick() => Picked.Add(Id);
}
