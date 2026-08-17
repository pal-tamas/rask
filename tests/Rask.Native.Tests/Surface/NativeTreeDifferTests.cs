using Rask.Native.Surface;

namespace Rask.Native.Tests.Surface;

/// <summary>
///     The tree differ in isolation. Every case asserts on the tree a backend ends up with after applying the
///     patches — not just on the patch list — because a patch list that looks right but replays wrong is the
///     failure mode that would reach a device.
/// </summary>
public class NativeTreeDifferTests
{
    private static NativeNode Node(
        NativeNodeKind kind, string? key = null, string? text = null, params NativeNode[] children) =>
        new()
        {
            Kind = kind,
            Key = key,
            Props = text is null ? [] : [new NativeProp(NativePropId.Text, NativePropValue.FromText(text))],
            Children = children,
        };

    private static NativeNode Label(string text, string? key = null) =>
        Node(NativeNodeKind.Label, key, text);

    private static NativeNode Screen(params NativeNode[] children) =>
        Node(NativeNodeKind.Screen, children: children);

    // Replays the patches the way FakeNativeSurface (and a real backend) does, then reads the labels back.
    private static List<string?> ApplyAndRead(NativeNode from, NativeNode to)
    {
        var patches = NativeTreeDiffer.Diff(from, to);
        Assert.NotNull(patches);

        var surface = new Infrastructure.FakeNativeSurface();
        surface.MountAsync(from);
        surface.PatchAsync(patches!);

        var texts = new List<string?>();
        foreach (var child in surface.Tree!.Children)
        {
            texts.Add(child.Text);
        }

        return texts;
    }

    [Fact]
    public void IdenticalTrees_ProduceNoPatches()
    {
        var patches = NativeTreeDiffer.Diff(Screen(Label("a"), Label("b")), Screen(Label("a"), Label("b")));

        Assert.Empty(patches!);
    }

    [Fact]
    public void ChangedText_ProducesOneSetPropsAtTheRightPath()
    {
        var patches = NativeTreeDiffer.Diff(Screen(Label("a"), Label("b")), Screen(Label("a"), Label("B")))!;

        var patch = Assert.Single(patches);
        Assert.Equal(NativePatchKind.SetProps, patch.Kind);
        Assert.Equal([1], patch.Path);
        var prop = Assert.Single(patch.Props!);
        Assert.Equal(NativePropId.Text, prop.Id);
        Assert.Equal("B", prop.Value.Text);
    }

    [Fact]
    public void RemovedProp_IsCarriedAsUnset_SoTheBackendResetsIt()
    {
        var before = Screen(new NativeNode
        {
            Kind = NativeNodeKind.Label,
            Props =
            [
                new NativeProp(NativePropId.Text, NativePropValue.FromText("hi")),
                new NativeProp(NativePropId.Color, NativePropValue.FromText("#ff0000ff")),
            ],
        });
        var after = Screen(Label("hi"));

        var patches = NativeTreeDiffer.Diff(before, after)!;

        var prop = Assert.Single(Assert.Single(patches).Props!);
        Assert.Equal(NativePropId.Color, prop.Id);
        Assert.Equal(NativePropKind.None, prop.Value.Kind);
    }

    [Fact]
    public void ChangedKind_ReplacesRatherThanPatching()
    {
        var patches = NativeTreeDiffer.Diff(
            Screen(Label("a")), Screen(Node(NativeNodeKind.Button, text: "a")))!;

        var patch = Assert.Single(patches);
        Assert.Equal(NativePatchKind.Replace, patch.Kind);
        Assert.Equal([0], patch.Path);
        Assert.Equal(NativeNodeKind.Button, patch.Node!.Kind);
    }

    [Fact]
    public void ChangedRootKind_CannotBePatched_SoTheCallerRemounts()
    {
        var patches = NativeTreeDiffer.Diff(Screen(), Node(NativeNodeKind.Stack));

        Assert.Null(patches);
    }

    [Fact]
    public void UnkeyedChildren_AppendAndTrimAtTheTail()
    {
        Assert.Equal(["a", "b", "c"], ApplyAndRead(Screen(Label("a")), Screen(Label("a"), Label("b"), Label("c"))));
        Assert.Equal(["a"], ApplyAndRead(Screen(Label("a"), Label("b"), Label("c")), Screen(Label("a"))));
    }

    [Fact]
    public void KeyedChildren_ReorderByMoving_NotByRewritingEveryRow()
    {
        var before = Screen(Label("a", "1"), Label("b", "2"), Label("c", "3"));
        var after = Screen(Label("c", "3"), Label("a", "1"), Label("b", "2"));

        var patches = NativeTreeDiffer.Diff(before, after)!;

        // The rows moved; not one of them had its text rewritten.
        Assert.All(patches, p => Assert.Equal(NativePatchKind.Move, p.Kind));
        Assert.Equal(["c", "a", "b"], ApplyAndRead(before, after));
    }

    [Fact]
    public void KeyedChildren_RemoveFromTheMiddle_KeepsTheRest()
    {
        var before = Screen(Label("a", "1"), Label("b", "2"), Label("c", "3"));
        var after = Screen(Label("a", "1"), Label("c", "3"));

        Assert.Equal(["a", "c"], ApplyAndRead(before, after));
    }

    [Fact]
    public void KeyedChildren_InsertIntoTheMiddle_ShiftsTheRest()
    {
        var before = Screen(Label("a", "1"), Label("c", "3"));
        var after = Screen(Label("a", "1"), Label("b", "2"), Label("c", "3"));

        Assert.Equal(["a", "b", "c"], ApplyAndRead(before, after));
    }

    [Fact]
    public void KeyedChildren_SimultaneousInsertRemoveAndReorder_Replays()
    {
        var before = Screen(Label("a", "1"), Label("b", "2"), Label("c", "3"), Label("d", "4"));
        var after = Screen(Label("d", "4"), Label("x", "9"), Label("b", "2"));

        Assert.Equal(["d", "x", "b"], ApplyAndRead(before, after));
    }

    [Fact]
    public void KeyedRow_ThatMovedAndChanged_GetsBothOps()
    {
        var before = Screen(Label("a", "1"), Label("b", "2"));
        var after = Screen(Label("B", "2"), Label("a", "1"));

        Assert.Equal(["B", "a"], ApplyAndRead(before, after));
    }

    [Fact]
    public void GainingKeys_RemountsTheRows_RatherThanMatchingAgainstPositions()
    {
        // An unkeyed child's identity is its position and a keyed one's is its key; they must never match,
        // or adding keys to an existing list would silently pair up unrelated rows.
        var before = Screen(Label("a"), Label("b"));
        var after = Screen(Label("a", "1"), Label("b", "2"));

        var patches = NativeTreeDiffer.Diff(before, after)!;

        Assert.Equal(["a", "b"], ApplyAndRead(before, after));
        Assert.Contains(patches, p => p.Kind is NativePatchKind.Insert);
    }

    [Fact]
    public void NestedChange_AddressesTheDeepPath()
    {
        var before = Screen(Node(NativeNodeKind.Stack, children: [Label("a"), Label("b")]));
        var after = Screen(Node(NativeNodeKind.Stack, children: [Label("a"), Label("B")]));

        var patch = Assert.Single(NativeTreeDiffer.Diff(before, after)!);

        Assert.Equal([0, 1], patch.Path);
    }
}
