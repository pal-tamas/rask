#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// #685. A child's identity inside its parent is its ORDINAL among entry-built children, and `Key` does
// not participate — the parent's child map never reads it. So inserting an item at the top of a keyed
// list hands every later row the NEXT row's instance: private fields, Mount subscriptions and any
// state the row holds itself move with the position rather than with the item. That is precisely the
// state-follows-position bug `Key` exists to prevent, one layer below where `Key` is consulted.
//
// The rows are built through the chain ENTRY, which is the only way to reach GetOrCreateChild at all —
// constructing one with `new` bypasses the machinery entirely and would prove nothing. The entry is
// named through its host (`RaskEntriesRask_Core_Tests`) rather than by simple name, because a type in
// scope beats an injected entry of the same name (the #684 family).
public class KeyedChildIdentityTests
{
    [Fact]
    public void InsertingAtTheTop_KeepsEachKeyedRowsOwnInstance()
    {
        KeyedRow.MountCount = 0;
        var list = new KeyedList();
        list.Ids.AddRange([1, 2, 3]);

        // Each row records which instance it is the first time it mounts, so the rendered text is
        // "<item id>:<instance number>" — a row that got re-created shows a NEW instance number.
        Assert.Equal(Rows((1, 1), (2, 2), (3, 3)), list.RenderAsLiveRoot());

        list.Ids.Insert(0, 0);

        // Only item 0 is new, so only it may mount (as instance #4). Items 1-3 must still be the very
        // instances that mounted above — they are the same items, merely one position lower.
        Assert.Equal(Rows((0, 4), (1, 1), (2, 2), (3, 3)), list.RenderAsLiveRoot());
    }

    [Fact]
    public void RemovingFromTheTop_KeepsEachKeyedRowsOwnInstance()
    {
        KeyedRow.MountCount = 0;
        var list = new KeyedList();
        list.Ids.AddRange([1, 2, 3]);

        Assert.Equal(Rows((1, 1), (2, 2), (3, 3)), list.RenderAsLiveRoot());

        list.Ids.RemoveAt(0);

        // Nothing new mounts: 2 and 3 are the same items and keep the instances they had.
        Assert.Equal(Rows((2, 2), (3, 3)), list.RenderAsLiveRoot());
    }

    [Fact]
    public void ReorderingKeyedRows_MovesTheInstanceWithTheItem()
    {
        KeyedRow.MountCount = 0;
        var list = new KeyedList();
        list.Ids.AddRange([1, 2, 3]);

        Assert.Equal(Rows((1, 1), (2, 2), (3, 3)), list.RenderAsLiveRoot());

        list.Ids.Reverse();

        // A reorder is the same three items in a different order — no row may re-mount.
        Assert.Equal(Rows((3, 3), (2, 2), (1, 1)), list.RenderAsLiveRoot());
    }

    [Fact]
    public void KeyWrittenLast_StillKeepsTheInstance_AndItsSteps()
    {
        // #1118: the steps written before Key land on the provisional instance; when the key claims the one
        // that mounted before, they are carried across. A reorder therefore keeps both the instance AND
        // the values this render wrote — before the fix the kept row showed the Id it was first built with.
        KeyedRow.MountCount = 0;
        var list = new KeyLastList();
        list.Ids.AddRange([1, 2, 3]);

        Assert.Equal(Rows((1, 1), (2, 2), (3, 3)), list.RenderAsLiveRoot());

        list.Ids.Reverse();
        Assert.Equal(Rows((3, 3), (2, 2), (1, 1)), list.RenderAsLiveRoot());

        list.Ids.Insert(0, 9);
        Assert.Equal(Rows((9, 4), (3, 3), (2, 2), (1, 1)), list.RenderAsLiveRoot());
    }

    [Fact]
    public void KeyWrittenLast_AfterAStepThatBuildsAnotherChild_RefilesTheRightSlot()
    {
        // A step's ARGUMENT can itself be a chain — here an element built between the row's entry and its
        // Key. That entry moves the parent's "last child slot" onto itself, so the claim must re-file the
        // row's OWN slot rather than whichever was filed last, or the kept row is never seen as alive and is
        // torn down under the item it belongs to.
        SlottedRow.MountCount = 0;
        SlottedRow.Unmounts = 0;
        var list = new SlottedList();
        list.Ids.AddRange([1, 2]);

        list.RenderAsLiveRoot();
        list.Ids.Reverse();
        var html = list.RenderAsLiveRoot();

        Assert.Contains("2:2", html, StringComparison.Ordinal);
        Assert.Contains("1:1", html, StringComparison.Ordinal);
        Assert.Equal(0, SlottedRow.Unmounts);
    }

    [Fact]
    public void KeyWrittenAfterTheChildren_KeepsTheChildren_AndClaimsUnderTheRowsOwnType()
    {
        // `Row.Id(id)[body].Key(id)`: the indexer hands back Component, so this Key is the generic one over
        // Component. The claim must still file the row under ITS type, and carry the children the indexer
        // already wrote rather than clearing them on the instance it keeps (#1118 review).
        SlottedRow.MountCount = 0;
        SlottedRow.Unmounts = 0;
        var list = new ChildrenFirstList();
        list.Ids.AddRange([1, 2]);

        list.RenderAsLiveRoot();
        list.Ids.Reverse();
        var html = list.RenderAsLiveRoot();

        Assert.Contains("2:2<b>body 2</b>", html, StringComparison.Ordinal);
        Assert.Contains("1:1<b>body 1</b>", html, StringComparison.Ordinal);
        Assert.Equal(0, SlottedRow.Unmounts);
    }

    // The Key step also emits data-rask-key, so spell the expected HTML once here and keep the
    // assertions about which INSTANCE each item is holding.
    private static string Rows(params (int Id, int Instance)[] rows) =>
        "<div>"
        + string.Concat(rows.Select(r => $"<i data-rask-key=\"{r.Id}\">{r.Id}:{r.Instance}</i>"))
        + "</div>";
}

/// <summary>Renders one entry-built, keyed row per id.</summary>
public sealed partial class KeyedList : Component
{
    public List<int> Ids { get; } = [];

    protected override Component? Render() =>
        // Key first, the way it reads best: it names which item this is. KeyLastList below writes it last,
        // which is just as correct since #1118.
        Div[Ids.Select(id => (Component)global::RaskEntriesRask_Core_Tests.KeyedRow.Key(id).Id(id))];
}

/// <summary>The same rows with Key written LAST (#1118).</summary>
public sealed partial class KeyLastList : Component
{
    public List<int> Ids { get; } = [];

    protected override Component? Render() =>
        Div[Ids.Select(id => (Component)global::RaskEntriesRask_Core_Tests.KeyedRow.Id(id).Key(id))];
}

/// <summary>Key written last, after a step whose argument is itself built by an entry.</summary>
public sealed partial class SlottedList : Component
{
    public List<int> Ids { get; } = [];

    protected override Component? Render() =>
        Div[Ids.Select(id => (Component)global::RaskEntriesRask_Core_Tests.SlottedRow.Id(id).Badge(Span["b"]).Key(id))];
}

/// <summary>Children through the indexer, THEN Key — the generic Key over Component.</summary>
public sealed partial class ChildrenFirstList : Component
{
    public List<int> Ids { get; } = [];

    protected override Component? Render() =>
        Div[Ids.Select(id => global::RaskEntriesRask_Core_Tests.SlottedRow.Id(id)[B[$"body {id}"]].Key(id))];
}

/// <summary>A keyed row with a component-valued prop, counting its mounts and unmounts.</summary>
public sealed partial class SlottedRow : Component
{
    internal static int MountCount;
    internal static int Unmounts;

    private int _instance;

    public required int Id { get; set; }

    public Component? Badge { get; set; }

    protected override Task Mount()
    {
        _instance = ++MountCount;
        return Task.CompletedTask;
    }

    protected override Task Unmount()
    {
        Unmounts++;
        return Task.CompletedTask;
    }

    protected override Component? Render() => I[$"{Id}:{_instance}", Badge, Children ?? []];
}

/// <summary>
///     Holds state of its own — the instance number it was handed when it first mounted. If the parent
///     re-creates it, that number changes, which is exactly what the assertions read.
/// </summary>
public sealed partial class KeyedRow : Component
{
    internal static int MountCount;

    private int _instance;

    public required int Id { get; set; }

    protected override Task Mount()
    {
        _instance = ++MountCount;
        return Task.CompletedTask;
    }

    protected override Component? Render() => I[$"{Id}:{_instance}"];
}
