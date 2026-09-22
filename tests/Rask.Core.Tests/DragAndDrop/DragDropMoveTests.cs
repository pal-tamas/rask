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
    public void Dragging_down_onto_the_immediate_neighbour_in_the_same_list_moves_the_item()
    {
        var list = Fruits();

        // Regression: this used to be a no-op (drop-before == original slot).
        Move(0, 1).ApplyTo(list);

        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void Dragging_down_onto_the_last_slot_in_the_same_list_reaches_the_bottom()
    {
        var list = Fruits();

        Move(0, 4).ApplyTo(list);

        Assert.Equal(["Banana", "Cherry", "Date", "Elderberry", "Apple"], list);
    }

    [Fact]
    public void Dragging_down_into_the_middle_of_the_same_list_lands_after_the_target()
    {
        var list = Fruits();

        Move(0, 2).ApplyTo(list);

        Assert.Equal(["Banana", "Cherry", "Apple", "Date", "Elderberry"], list);
    }

    [Fact]
    public void Dragging_up_onto_the_neighbour_in_the_same_list_moves_the_item()
    {
        var list = Fruits();

        Move(1, 0).ApplyTo(list);

        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void Dragging_up_to_the_top_of_the_same_list_reaches_the_top()
    {
        var list = Fruits();

        Move(4, 0).ApplyTo(list);

        Assert.Equal(["Elderberry", "Apple", "Banana", "Cherry", "Date"], list);
    }

    [Fact]
    public void Dragging_up_into_the_middle_of_the_same_list_lands_before_the_target()
    {
        var list = Fruits();

        Move(4, 2).ApplyTo(list);

        Assert.Equal(["Apple", "Banana", "Elderberry", "Cherry", "Date"], list);
    }

    [Fact]
    public void The_single_list_overload_delegates_to_the_two_list_one()
    {
        var list = Fruits();

        Move(0, 1).ApplyTo(list, list);

        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void A_cross_list_move_inserts_before_the_target()
    {
        var from = new List<string> { "A", "B", "C" };
        var to = new List<string> { "X", "Y", "Z" };

        new DragDropMove("from", 1, "to", 1).ApplyTo(from, to);

        Assert.Equal(["A", "C"], from);
        Assert.Equal(["X", "B", "Y", "Z"], to);
    }

    [Fact]
    public void A_cross_list_drop_at_the_end_appends()
    {
        var from = new List<string> { "A", "B", "C" };
        var to = new List<string> { "X", "Y" };

        new DragDropMove("from", 0, "to", to.Count).ApplyTo(from, to);

        Assert.Equal(["B", "C"], from);
        Assert.Equal(["X", "Y", "A"], to);
    }

    [Fact]
    public void A_cross_list_move_into_an_empty_target_inserts_at_zero()
    {
        var from = new List<string> { "A", "B" };
        var to = new List<string>();

        new DragDropMove("from", 0, "to", 5).ApplyTo(from, to);

        Assert.Equal(["B"], from);
        Assert.Equal(["A"], to);
    }

    [Fact]
    public void Moving_an_item_onto_its_own_slot_in_the_same_list_is_a_no_op()
    {
        var list = Fruits();

        Move(2, 2).ApplyTo(list);

        Assert.Equal(["Apple", "Banana", "Cherry", "Date", "Elderberry"], list);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void An_out_of_range_from_index_is_a_no_op(int fromIndex)
    {
        var list = Fruits();

        Move(fromIndex, 0).ApplyTo(list);

        Assert.Equal(["Apple", "Banana", "Cherry", "Date", "Elderberry"], list);
    }

    [Fact]
    public void Applying_to_a_null_list_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Move(0, 1).ApplyTo((IList<string>)null!));
        Assert.Throws<ArgumentNullException>(() => Move(0, 1).ApplyTo(new List<string> { "A" }, null!));
    }
}
