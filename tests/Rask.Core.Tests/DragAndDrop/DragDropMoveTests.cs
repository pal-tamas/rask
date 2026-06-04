using Rask.Core.DragAndDrop;

namespace Rask.Core.Tests.DragAndDrop;

// DragDropMove.ApplyTo owns the reorder math every DragDrop consumer relies on. It is
// direction-aware by construction: dragging an item down drops it *after* the target, dragging
// up drops it *before* the target, and either end of the list is reachable. These guard the
// regression where dragging down onto the next neighbour was a no-op and the last slot was
// unreachable.
public class DragDropMoveTests
{
    private static List<string> Fruits() => ["Apple", "Banana", "Cherry", "Date", "Elderberry"];

    private static DragDropMove Move(int from, int to, string zone = "z") => new(zone, from, zone, to);

    [Fact]
    public void ApplyTo_SameList_DownOntoImmediateNeighbour_MovesItem()
    {
        var list = Fruits();

        // Regression: this used to be a no-op (drop-before == original slot).
        Move(0, 1).ApplyTo(list);

        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void ApplyTo_SameList_DownOntoLastSlot_ReachesBottom()
    {
        var list = Fruits();

        Move(0, 4).ApplyTo(list);

        Assert.Equal(["Banana", "Cherry", "Date", "Elderberry", "Apple"], list);
    }

    [Fact]
    public void ApplyTo_SameList_DownIntoMiddle_LandsAfterTarget()
    {
        var list = Fruits();

        Move(0, 2).ApplyTo(list);

        Assert.Equal(["Banana", "Cherry", "Apple", "Date", "Elderberry"], list);
    }

    [Fact]
    public void ApplyTo_SameList_UpOntoNeighbour_MovesItem()
    {
        var list = Fruits();

        Move(1, 0).ApplyTo(list);

        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void ApplyTo_SameList_UpToTop_ReachesTop()
    {
        var list = Fruits();

        Move(4, 0).ApplyTo(list);

        Assert.Equal(["Elderberry", "Apple", "Banana", "Cherry", "Date"], list);
    }

    [Fact]
    public void ApplyTo_SameList_UpIntoMiddle_LandsBeforeTarget()
    {
        var list = Fruits();

        Move(4, 2).ApplyTo(list);

        Assert.Equal(["Apple", "Banana", "Elderberry", "Cherry", "Date"], list);
    }

    [Fact]
    public void ApplyTo_SingleListOverload_DelegatesToTwoArg()
    {
        var list = Fruits();

        Move(0, 1).ApplyTo(list, list);

        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void ApplyTo_CrossList_InsertsBeforeTarget()
    {
        var from = new List<string> { "A", "B", "C" };
        var to = new List<string> { "X", "Y", "Z" };

        new DragDropMove("from", 1, "to", 1).ApplyTo(from, to);

        Assert.Equal(["A", "C"], from);
        Assert.Equal(["X", "B", "Y", "Z"], to);
    }

    [Fact]
    public void ApplyTo_CrossList_DropAtEnd_Appends()
    {
        var from = new List<string> { "A", "B", "C" };
        var to = new List<string> { "X", "Y" };

        new DragDropMove("from", 0, "to", to.Count).ApplyTo(from, to);

        Assert.Equal(["B", "C"], from);
        Assert.Equal(["X", "Y", "A"], to);
    }

    [Fact]
    public void ApplyTo_CrossList_EmptyTarget_InsertsAtZero()
    {
        var from = new List<string> { "A", "B" };
        var to = new List<string>();

        new DragDropMove("from", 0, "to", 5).ApplyTo(from, to);

        Assert.Equal(["B"], from);
        Assert.Equal(["A"], to);
    }

    [Fact]
    public void ApplyTo_SameList_FromEqualsTo_IsNoOp()
    {
        var list = Fruits();

        Move(2, 2).ApplyTo(list);

        Assert.Equal(["Apple", "Banana", "Cherry", "Date", "Elderberry"], list);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void ApplyTo_FromIndexOutOfRange_IsNoOp(int fromIndex)
    {
        var list = Fruits();

        Move(fromIndex, 0).ApplyTo(list);

        Assert.Equal(["Apple", "Banana", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void ApplyTo_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Move(0, 1).ApplyTo((IList<string>)null!));
        Assert.Throws<ArgumentNullException>(() => Move(0, 1).ApplyTo(new List<string> { "A" }, null!));
    }
}
