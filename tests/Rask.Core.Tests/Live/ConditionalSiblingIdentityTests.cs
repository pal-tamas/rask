using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Entries = RaskEntriesRask_Core_Tests;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it
#pragma warning disable RASK022 // rows without a Key, alone and beside keyed ones, are the subject of these tests

namespace Rask.Core.Tests.Live;

// A child without a Key is identified by its ORDER AMONG THE CHILDREN OF ITS TYPE that its owner writes —
// the owner being the component whose Render() builds it, not the element it sits under. It used to be the
// order among ALL the owner's children, so one child coming or going rebuilt every component written after
// it: fields reset, OnMount again, OnUnmount for an instance that never left the screen.
//
// Every row prints "<id>:<instance number>", the number being handed out when it mounts — a row that was
// built again shows a new one.
public partial class ConditionalSiblingIdentityTests : global::Rask.Core.RaskMarkup
{
    private readonly RowTally _rows = new();
    private readonly RowTally _notes = new();

    [Fact]
    public void A_paragraph_that_comes_and_goes_ahead_of_two_rows_leaves_both_mounted()
    {
        var show = false;
        var view = new StubComponent(() => Div[show ? P["note"] : null, Row(7), Span["x"], Row(8)]);
        var first = view.RenderAsLiveRoot();

        show = true;
        var shown = view.RenderAsLiveRoot();
        show = false;
        var hidden = view.RenderAsLiveRoot();

        Assert.Equal("<div><i>7:1</i><span>x</span><i>8:2</i></div>", first);
        Assert.Equal("<div><p>note</p><i>7:1</i><span>x</span><i>8:2</i></div>", shown);
        Assert.Equal(first, hidden);
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void A_component_of_another_type_that_comes_and_goes_ahead_of_two_rows_leaves_both_mounted()
    {
        var show = false;
        var view = new StubComponent(() => Div[show ? Note() : null, Row(7), Row(8)]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());
        show = false;
        var hidden = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1 8:2", first);
        Assert.Equal(first, shown);
        Assert.Equal(first, hidden);
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
        Assert.Equal((1, 1), (_notes.Mounts, _notes.Unmounts));
    }

    // Unavoidable without a Key: nothing but the order tells one row from the next.
    [Fact]
    public void A_row_that_appears_ahead_of_other_rows_takes_the_first_ones_instance_and_shifts_the_rest()
    {
        var show = false;
        var view = new StubComponent(() => Div[show ? Row(1) : null, Row(7), Row(8)]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());
        show = false;
        var hidden = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1 8:2", first);
        Assert.Equal("1:1 7:2 8:3", shown);
        Assert.Equal(first, hidden);
        Assert.Equal((3, 1), (_rows.Mounts, _rows.Unmounts));
        Assert.Equal(["8:3"], _rows.Left);
    }

    [Fact]
    public void A_list_without_keys_that_gains_an_item_at_the_top_shifts_every_row_by_one()
    {
        List<int> ids = [7, 8];
        var view = new StubComponent(() => Ul[ids.Select(Row)]);
        var first = Pairs(view.RenderAsLiveRoot());

        ids.Insert(0, 1);
        var grown = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1 8:2", first);
        Assert.Equal("1:1 7:2 8:3", grown);
        Assert.Equal((3, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void An_element_with_children_of_its_own_that_appears_ahead_of_a_row_leaves_it_mounted()
    {
        var show = false;
        var view = new StubComponent(() => Div[show ? Div[Strong["Saved"], " just now"] : null, Row(7)]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = view.RenderAsLiveRoot();

        Assert.Equal("7:1", first);
        Assert.Equal("<div><div><strong>Saved</strong> just now</div><i>7:1</i></div>", shown);
        Assert.Equal((1, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void A_child_that_appears_inside_a_nested_element_leaves_the_rows_beside_and_after_it_mounted()
    {
        var show = false;
        var view = new StubComponent(() => Div[Section[show ? Note() : null, Row(7)], Row(8)]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());
        show = false;
        var hidden = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1 8:2", first);
        Assert.Equal(first, shown);
        Assert.Equal(first, hidden);
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
    }

    // ONE sequence per type for the whole owner, in the order Render() writes them: the element a child sits
    // under is not part of who it is, because the child is built before that element is given its children.
    [Fact]
    public void Rows_under_two_different_elements_of_one_owner_are_counted_in_one_sequence()
    {
        var show = false;
        var view = new StubComponent(() => Div[Section[show ? Row(1) : null], Aside[Row(7)]]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1", first);
        Assert.Equal("1:1 7:2", shown);
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void Two_children_that_come_and_go_independently_leave_the_rows_around_them_mounted()
    {
        var (callout, note) = (false, false);
        var view = new StubComponent(() => Div[callout ? P["callout"] : null, Row(7), note ? Note() : null, Row(8)]);
        var first = Pairs(view.RenderAsLiveRoot());
        List<string> later = [];

        foreach (var (showCallout, showNote) in new[] { (true, false), (true, true), (false, true), (false, false) })
        {
            (callout, note) = (showCallout, showNote);
            later.Add(Pairs(view.RenderAsLiveRoot()));
        }

        Assert.Equal("7:1 8:2", first);
        Assert.All(later, pairs => Assert.Equal(first, pairs));
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
        Assert.Equal((1, 1), (_notes.Mounts, _notes.Unmounts));
    }

    [Fact]
    public void A_fragment_that_gains_a_child_ahead_of_a_row_leaves_it_mounted()
    {
        var show = false;
        var view = new StubComponent(() => Div[Fragment[show ? P["note"] : null, Row(7)], Row(8)]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1 8:2", first);
        Assert.Equal(first, shown);
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void A_generic_component_closed_over_another_type_is_another_type()
    {
        var show = false;
        var view = new StubComponent(() => Div[
            show ? Entries.TypedRow.Value(1).Tally(_notes) : null,
            Entries.TypedRow.Value("a").Tally(_rows)
        ]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("a:1", first);
        Assert.Equal("1:1 a:1", shown);
        Assert.Equal((1, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void A_wrapping_component_keeps_its_own_rows_and_the_ones_handed_to_it_when_a_child_appears()
    {
        var show = false;
        var view = new StubComponent(() => Entries.TallyCard.Tally(_rows).Flagged(show)[show ? P["note"] : null, Row(7)]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());
        show = false;
        var hidden = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1 100:2", first);
        Assert.Equal(first, shown);
        Assert.Equal(first, hidden);
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void Keyed_and_unkeyed_rows_all_stay_mounted_when_children_of_other_types_come_and_go_around_them()
    {
        var show = false;
        var view = new StubComponent(() => Div[
            show ? P["callout"] : null,
            Entries.TallyRow.Key("k").Tally(_rows).Id(1),
            show ? Note() : null,
            Row(100),
            Entries.TallyRow.Key("j").Tally(_rows).Id(2),
            Row(101)
        ]);
        var first = Pairs(view.RenderAsLiveRoot());

        show = true;
        var shown = Pairs(view.RenderAsLiveRoot());
        show = false;
        var hidden = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("1:1 100:2 2:3 101:4", first);
        Assert.Equal(first, shown);
        Assert.Equal(first, hidden);
        Assert.Equal((4, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public async Task A_callout_a_click_brings_in_leaves_the_count_a_later_component_holds()
    {
        var view = new StubComponent(() => Entries.TogglingPanel.Tally(_rows));
        var count = Handler(view.RenderAsLiveRoot(), "count");
        await Click(view, count);
        var counted = view.RenderAsLiveRoot();

        await Click(view, Handler(counted, "toggle"));
        var shown = view.RenderAsLiveRoot();
        await Click(view, Handler(shown, "toggle"));
        var hidden = view.RenderAsLiveRoot();

        Assert.Contains(">count 1</button>", counted, StringComparison.Ordinal);
        Assert.Contains("<p>callout</p>", shown, StringComparison.Ordinal);
        Assert.Contains(">count 1</button>", shown, StringComparison.Ordinal);
        Assert.Equal(counted, hidden);
        Assert.Equal("7:1", Pairs(shown));
        Assert.Equal((1, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public async Task A_callout_a_finished_load_brings_in_leaves_the_later_components_mounted()
    {
        var gate = new TaskCompletionSource();
        var view = new StubComponent(() => Entries.LoadingPanel.Tally(_rows).Gate(gate));
        var loading = view.RenderAsLiveRoot();

        gate.SetResult();
        await _rows.Loaded.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var loaded = view.RenderAsLiveRoot();

        Assert.Equal("<div><i>7:1</i><i>8:2</i></div>", loading);
        Assert.Equal("<div><p>loaded</p><i>7:1</i><i>8:2</i></div>", loaded);
        Assert.Equal((2, 0), (_rows.Mounts, _rows.Unmounts));
    }

    [Fact]
    public void A_row_that_leaves_is_unmounted_once_and_the_rows_that_stay_never_are()
    {
        var show = true;
        var view = new StubComponent(() => Div[Row(7), show ? Note() : null, show ? Row(8) : null]);
        view.RenderAsLiveRoot();

        show = false;
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();
        var last = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1", last);
        Assert.Equal(["8:2"], _rows.Left);
        Assert.Equal(1, _notes.Unmounts);
    }

    [Fact]
    public void Rows_that_leave_together_are_unmounted_in_the_order_they_were_written()
    {
        var show = true;
        var view = new StubComponent(() => Div[Row(7), show ? Section[Row(8), Row(9)] : null, show ? Row(10) : null]);
        view.RenderAsLiveRoot();

        show = false;
        view.RenderAsLiveRoot();

        Assert.Equal(["8:2", "9:3", "10:4"], _rows.Left);
    }

    [Fact]
    public void A_type_that_is_no_longer_written_lets_go_of_the_instances_it_had()
    {
        var show = true;
        var view = new StubComponent(() => Div[show ? Row(7) : null, show ? Row(8) : null, Span["x"]]);
        RenderTwice(view);

        show = false;
        RenderTwice(view);
        RenderTwice(view);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(2, _rows.Unmounts);
        Assert.Equal(2, _rows.Instances.Count);
        Assert.All(_rows.Instances, row => Assert.False(row.TryGetTarget(out _)));
    }

    [Fact]
    public void A_type_that_comes_back_after_leaving_starts_again_from_instances_that_never_mounted()
    {
        var show = true;
        var view = new StubComponent(() => Div[show ? Row(7) : null, Span["x"], show ? Row(8) : null]);
        var first = Pairs(view.RenderAsLiveRoot());
        show = false;
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

        show = true;
        var back = Pairs(view.RenderAsLiveRoot());

        Assert.Equal("7:1 8:2", first);
        Assert.Equal("7:3 8:4", back);
        Assert.Equal((4, 2), (_rows.Mounts, _rows.Unmounts));
    }

    // Kept out of the test's own frame, where a debug build would hold on to what a render returned.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RenderTwice(StubComponent view)
    {
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();
    }

    private Component Row(int id) => Entries.TallyRow.Tally(_rows).Id(id);

    private Component Note() => Entries.TallyNote.Tally(_notes);

    private static string Handler(string html, string label) =>
        Regex.Match(html, $"data-rask-on-click=\"([^\"]+)\">{label}").Groups[1].Value;

    private static async Task Click(StubComponent view, string handler)
    {
        using var payload = JsonDocument.Parse("""{"type":"click"}""");
        Assert.True(await view.TryInvokeHandlerAsync(handler, payload.RootElement));
    }

    private static string Pairs(string html) => string.Join(' ', PairPattern().Matches(html).Select(m => m.Value));

    [GeneratedRegex(@"\w+:\d+")]
    private static partial Regex PairPattern();
}

/// <summary>What the rows of one test did, kept by the test rather than in statics another class could share.</summary>
public sealed class RowTally
{
    public int Mounts { get; set; }

    public int Unmounts { get; set; }

    /// <summary>Each row that unmounted, as it last printed itself, in the order they went.</summary>
    public List<string> Left { get; } = [];

    public List<WeakReference<Component>> Instances { get; } = [];

    public TaskCompletionSource Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Prints the instance number it was handed when it mounted.</summary>
public sealed partial class TallyRow : Component
{
    private int _instance;

    public required RowTally Tally { get; set; }

    public required int Id { get; set; }

    protected override Task OnMount()
    {
        _instance = ++Tally.Mounts;
        Tally.Instances.Add(new WeakReference<Component>(this));
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        Tally.Unmounts++;
        Tally.Left.Add($"{Id}:{_instance}");
        return Task.CompletedTask;
    }

    protected override Component? Render() => I[$"{Id}:{_instance}"];
}

/// <summary>A stateful component of ANOTHER type than the rows it is written among.</summary>
public sealed partial class TallyNote : Component
{
    public required RowTally Tally { get; set; }

    protected override Task OnMount()
    {
        Tally.Mounts++;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        Tally.Unmounts++;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Em["note"];
}

/// <summary>A generic row: each closed type is a type of its own.</summary>
public sealed partial class TypedRow<T> : Component
{
    private int _instance;

    public required RowTally Tally { get; set; }

    public required T Value { get; set; }

    protected override Task OnMount()
    {
        _instance = ++Tally.Mounts;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        Tally.Unmounts++;
        return Task.CompletedTask;
    }

    protected override Component? Render() => I[$"{Value}:{_instance}"];
}

/// <summary>A component that wraps what it is handed, with a row and a conditional child of its own.</summary>
public sealed partial class TallyCard : Component
{
    public required RowTally Tally { get; set; }

    public required bool Flagged { get; set; }

    protected override Component? Render() =>
        Section[Flagged ? B["!"] : null, Children, Entries.TallyRow.Tally(Tally).Id(100)];
}

/// <summary>A callout its own button brings in, ahead of a component holding a count and a row.</summary>
public sealed partial class TogglingPanel : Component
{
    private bool _show;

    public required RowTally Tally { get; set; }

    protected override Component? Render() =>
        Div[
            _show ? P["callout"] : null,
            Button.OnClick(() => _show = !_show)["toggle"],
            Entries.HeldCount,
            Entries.TallyRow.Tally(Tally).Id(7)
        ];
}

/// <summary>Holds a count in a field of its own — gone the moment it is built again.</summary>
public sealed partial class HeldCount : Component
{
    private int _count;

    protected override Component? Render() => Button.OnClick(() => _count++)[$"count {_count}"];
}

/// <summary>A callout that appears once what OnMount waits for has arrived.</summary>
public sealed partial class LoadingPanel : Component
{
    private bool _loaded;

    public required RowTally Tally { get; set; }

    public required TaskCompletionSource Gate { get; set; }

    protected override async Task OnMount()
    {
        await Gate.Task;
        _loaded = true;
        Tally.Loaded.SetResult();
    }

    protected override Component? Render() =>
        Div[
            _loaded ? P["loaded"] : null,
            Entries.TallyRow.Tally(Tally).Id(7),
            Entries.TallyRow.Tally(Tally).Id(8)
        ];
}
