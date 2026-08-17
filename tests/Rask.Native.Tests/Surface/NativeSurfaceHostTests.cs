using Rask.Native.Surface;

namespace Rask.Native.Tests.Surface;

/// <summary>
///     The shared half of every surface backend: retained tree + patch replay. A platform head supplies only a
///     mapping table, so proving this here is what keeps the iOS and Android backends from each having to be
///     debugged on a device — a scrambled screen is the symptom of an ordering bug, and it would be invisible
///     in a patch list that reads correctly.
/// </summary>
public class NativeSurfaceHostTests
{
    [Fact]
    public void Mount_BuildsTheWholeTree_DepthFirstInOrder()
    {
        var ops = new FakeOps();
        var host = new NativeSurfaceHost<FakeView>(ops);

        host.Mount(Screen(Label("a"), Stack(Label("b"), Label("c"))));

        Assert.Equal("Screen[Label(a), Stack[Label(b), Label(c)]]", Render(host));
    }

    [Fact]
    public void SetProps_ReachesTheRightNode_AndUnsetResetsIt()
    {
        var ops = new FakeOps();
        var host = new NativeSurfaceHost<FakeView>(ops);
        host.Mount(Screen(Label("a"), Label("b")));

        host.Apply([
            new NativePatch
            {
                Kind = NativePatchKind.SetProps,
                Path = [1],
                Props = [new NativeProp(NativePropId.Text, NativePropValue.FromText("B"))],
            },
        ]);
        Assert.Equal("Screen[Label(a), Label(B)]", Render(host));

        host.Apply([
            new NativePatch
            {
                Kind = NativePatchKind.SetProps,
                Path = [1],
                Props = [new NativeProp(NativePropId.Text, NativePropValue.Unset)],
            },
        ]);
        Assert.Equal("Screen[Label(a), Label()]", Render(host));
    }

    [Fact]
    public void Replace_SwapsTheViewAtItsIndex_NotJustItsProps()
    {
        var ops = new FakeOps();
        var host = new NativeSurfaceHost<FakeView>(ops);
        host.Mount(Screen(Label("a"), Label("b")));

        host.Apply([
            new NativePatch
            {
                Kind = NativePatchKind.Replace,
                Path = [0],
                Node = new NativeNode { Kind = NativeNodeKind.Button, Props = TextProp("go") },
            },
        ]);

        Assert.Equal("Screen[Button(go), Label(b)]", Render(host));
        // The old view was detached, not left parented — a leaked view would still be on screen.
        Assert.Contains(ops.Log, e => e.StartsWith("remove:Label", StringComparison.Ordinal));
    }

    [Fact]
    public void InsertRemoveAndMove_ReplayInOrder_AgainstTheEvolvingList()
    {
        var ops = new FakeOps();
        var host = new NativeSurfaceHost<FakeView>(ops);
        host.Mount(Screen(Label("a"), Label("b"), Label("c")));

        // Exactly what the differ emits for a->c, b removed, x inserted: remove tail-first, then place.
        host.Apply([
            new NativePatch { Kind = NativePatchKind.Remove, Path = [], Index = 1 },
            new NativePatch { Kind = NativePatchKind.Move, Path = [], FromIndex = 1, Index = 0 },
            new NativePatch
            {
                Kind = NativePatchKind.Insert,
                Path = [],
                Index = 2,
                Node = new NativeNode { Kind = NativeNodeKind.Label, Props = TextProp("x") },
            },
        ]);

        Assert.Equal("Screen[Label(c), Label(a), Label(x)]", Render(host));
    }

    [Fact]
    public void Move_KeepsTheSameViewInstance_SoRowStateSurvivesAReorder()
    {
        var ops = new FakeOps();
        var host = new NativeSurfaceHost<FakeView>(ops);
        host.Mount(Screen(Label("a"), Label("b")));
        var before = ops.Created.Count;

        host.Apply([new NativePatch { Kind = NativePatchKind.Move, Path = [], FromIndex = 1, Index = 0 }]);

        Assert.Equal("Screen[Label(b), Label(a)]", Render(host));
        Assert.Equal(before, ops.Created.Count); // nothing was rebuilt — that is the point of a move
    }

    [Fact]
    public void NestedPath_ResolvesThroughContainers()
    {
        var ops = new FakeOps();
        var host = new NativeSurfaceHost<FakeView>(ops);
        host.Mount(Screen(Stack(Stack(Label("deep")))));

        host.Apply([
            new NativePatch
            {
                Kind = NativePatchKind.SetProps,
                Path = [0, 0, 0],
                Props = [new NativeProp(NativePropId.Text, NativePropValue.FromText("reached"))],
            },
        ]);

        Assert.Equal("Screen[Stack[Stack[Label(reached)]]]", Render(host));
    }

    [Fact]
    public void PatchBeforeMount_FailsLoudly_RatherThanPaintingNothing()
    {
        var host = new NativeSurfaceHost<FakeView>(new FakeOps());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            host.Apply([new NativePatch { Kind = NativePatchKind.Remove, Path = [], Index = 0 }]));

        Assert.Contains("mounted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private static NativeProp[] TextProp(string text) =>
        [new NativeProp(NativePropId.Text, NativePropValue.FromText(text))];

    private static NativeNode Label(string text) =>
        new() { Kind = NativeNodeKind.Label, Props = TextProp(text) };

    private static NativeNode Stack(params NativeNode[] children) =>
        new() { Kind = NativeNodeKind.Stack, Children = children };

    private static NativeNode Screen(params NativeNode[] children) =>
        new() { Kind = NativeNodeKind.Screen, Children = children };

    private static string Render(NativeSurfaceHost<FakeView> host) => host.RootView!.ToString()!;

    // A view type that prints its own subtree, so an assertion reads like the screen it describes.
    private sealed class FakeView(NativeNodeKind kind)
    {
        public NativeNodeKind Kind { get; } = kind;
        public string? Text { get; set; }
        public List<FakeView> Children { get; } = [];

        public override string ToString() =>
            Children.Count > 0
                ? $"{Kind}[{string.Join(", ", Children)}]"
                : $"{Kind}({Text})";
    }

    private sealed class FakeOps : INativeViewOps<FakeView>
    {
        public List<FakeView> Created { get; } = [];
        public List<string> Log { get; } = [];

        public FakeView Create(NativeNodeKind kind)
        {
            var view = new FakeView(kind);
            Created.Add(view);
            return view;
        }

        public void SetProp(FakeView view, NativeNodeKind kind, NativePropId id, NativePropValue value)
        {
            if (id == NativePropId.Text)
            {
                view.Text = value.Kind == NativePropKind.None ? null : value.Text;
            }
        }

        public void InsertChild(FakeView parent, NativeNodeKind parentKind, FakeView child, int index)
        {
            parent.Children.Insert(index, child);
            Log.Add($"insert:{child.Kind}@{index}");
        }

        public void RemoveChild(FakeView parent, NativeNodeKind parentKind, FakeView child, int index)
        {
            parent.Children.RemoveAt(index);
            Log.Add($"remove:{child.Kind}@{index}");
        }

        public void MoveChild(FakeView parent, NativeNodeKind parentKind, FakeView child, int fromIndex, int toIndex)
        {
            parent.Children.RemoveAt(fromIndex);
            parent.Children.Insert(toIndex, child);
            Log.Add($"move:{child.Kind}:{fromIndex}->{toIndex}");
        }
    }
}
