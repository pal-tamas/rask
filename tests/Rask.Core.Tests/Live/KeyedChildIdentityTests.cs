#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// #685. A child's identity inside its parent is its ORDINAL among entry-built children, and `Key` does
// not participate — the parent's child map never reads it. So inserting an item at the top of a keyed
// list hands every later row the NEXT row's instance: private fields, OnMount subscriptions and any
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
        // Key opens the chain — it decides which instance is being built (RASK046). The pending-required
        // state carries a Key step of its own for exactly this, so a row with required props can still
        // settle its identity before anything is written to it.
        Div[Ids.Select(id => (Component)global::RaskEntriesRask_Core_Tests.KeyedRow.Key(id).Id(id))];
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

    protected override void OnMount() => _instance = ++MountCount;

    protected override Component? Render() => I[$"{Id}:{_instance}"];
}
