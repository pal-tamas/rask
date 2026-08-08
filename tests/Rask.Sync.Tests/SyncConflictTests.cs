namespace Rask.Sync.Tests;

// Last-writer-wins loses data by design, so the only question is whether anyone is told. These pin what
// gets reported and — just as important — what does not, because a conflict feed that cries wolf on every
// ordinary save is one users learn to ignore, which is the same outcome as not reporting at all.
public class SyncConflictTests
{
    private static readonly Guid Row = new("7f3a2b91-0000-0000-0000-000000000001");

    [Fact]
    public void Replacing_another_nodes_value_is_reported_with_both_sides()
    {
        var state = new SyncState();
        state.Apply(Op("node-a", 100, ("title", "\"from A\"")));

        var conflicts = state.Apply(Op("node-b", 200, ("title", "\"from B\"")));

        var conflict = Assert.Single(conflicts);
        Assert.Equal(SyncConflictKind.Overwritten, conflict.Kind);
        Assert.Equal("title", conflict.Field);
        Assert.Equal("\"from B\"", conflict.WinningValue);
        Assert.Equal("\"from A\"", conflict.LosingValue);
        Assert.Equal("node-b", conflict.WinningNode);
        Assert.Equal("node-a", conflict.LosingNode);
    }

    // The direction people forget: the edit that lost was the one that just arrived. Without this, a device
    // syncing after a long time offline is told nothing about its own discarded work.
    [Fact]
    public void An_arriving_edit_that_loses_is_reported_too()
    {
        var state = new SyncState();
        state.Apply(Op("node-b", 200, ("title", "\"from B\"")));

        var conflicts = state.Apply(Op("node-a", 100, ("title", "\"from A\"")));

        var conflict = Assert.Single(conflicts);
        Assert.Equal(SyncConflictKind.Discarded, conflict.Kind);
        Assert.Equal("\"from B\"", conflict.WinningValue);
        Assert.Equal("\"from A\"", conflict.LosingValue);
    }

    // A device overwriting its own earlier value is just editing. Reporting it would bury the real
    // conflicts under one entry per keystroke-debounced save.
    [Fact]
    public void A_node_overwriting_its_own_earlier_value_is_not_a_conflict()
    {
        var state = new SyncState();
        state.Apply(Op("node-a", 100, ("title", "\"first\"")));

        Assert.Empty(state.Apply(Op("node-a", 200, ("title", "\"second\""))));
    }

    // Two people independently ticking the same checkbox agree. Nothing was lost, so nothing is reported.
    [Fact]
    public void Two_nodes_writing_the_same_value_is_not_a_conflict()
    {
        var state = new SyncState();
        state.Apply(Op("node-a", 100, ("done", "true")));

        Assert.Empty(state.Apply(Op("node-b", 200, ("done", "true"))));
    }

    [Fact]
    public void A_first_write_to_an_empty_field_is_not_a_conflict()
    {
        Assert.Empty(new SyncState().Apply(Op("node-a", 100, ("title", "\"first\""))));
    }

    // Duplicate delivery must be silent, or a peer re-listing a bucket would manufacture a conflict storm
    // out of ops everyone already agreed on.
    [Fact]
    public void Replaying_the_same_op_reports_nothing_the_second_time()
    {
        var state = new SyncState();
        var op = Op("node-a", 100, ("title", "\"t\""));
        state.Apply(op);

        Assert.Empty(state.Apply(op));
    }

    // Losing a record entirely is the most damaging outcome and the least visible, since the row simply
    // stops being there.
    [Fact]
    public void A_delete_that_hides_another_nodes_edits_is_reported()
    {
        var state = new SyncState();
        state.Apply(Op("node-a", 100, ("title", "\"worth keeping\"")));

        var conflicts = state.Apply(SyncOp.Delete("Todo", Row, new HlcTimestamp(200, 0, "node-b")));

        var conflict = Assert.Single(conflicts);
        Assert.Equal(SyncConflictKind.DeleteHidEdits, conflict.Kind);
        Assert.Equal("\"worth keeping\"", conflict.LosingValue);
        Assert.Equal("node-b", conflict.WinningNode);
    }

    [Fact]
    public void A_node_deleting_a_row_only_it_had_touched_is_not_a_conflict()
    {
        var state = new SyncState();
        state.Apply(Op("node-a", 100, ("title", "\"mine\"")));

        Assert.Empty(state.Apply(SyncOp.Delete("Todo", Row, new HlcTimestamp(200, 0, "node-a"))));
    }

    [Fact]
    public void An_edit_that_revives_another_nodes_deleted_row_is_reported()
    {
        var state = new SyncState();
        state.Apply(SyncOp.Delete("Todo", Row, new HlcTimestamp(100, 0, "node-b")));

        var conflicts = state.Apply(Op("node-a", 200, ("title", "\"back\"")));

        var conflict = Assert.Single(conflicts);
        Assert.Equal(SyncConflictKind.EditRevivedDeleted, conflict.Kind);
        Assert.Equal("node-a", conflict.WinningNode);
        Assert.Equal("node-b", conflict.LosingNode);
    }

    [Fact]
    public void An_older_edit_arriving_after_a_delete_does_not_claim_to_have_revived_anything()
    {
        var state = new SyncState();
        state.Apply(SyncOp.Delete("Todo", Row, new HlcTimestamp(200, 0, "node-b")));

        var conflicts = state.Apply(Op("node-a", 100, ("title", "\"too late\"")));

        Assert.DoesNotContain(conflicts, c => c.Kind == SyncConflictKind.EditRevivedDeleted);
    }

    [Fact]
    public void Conflicts_are_reported_per_field_not_per_op()
    {
        var state = new SyncState();
        state.Apply(Op("node-a", 100, ("title", "\"A\""), ("notes", "\"A notes\"")));

        var conflicts = state.Apply(Op("node-b", 200, ("title", "\"B\""), ("notes", "\"B notes\"")));

        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, c => c.Field == "title");
        Assert.Contains(conflicts, c => c.Field == "notes");
    }

    private static SyncOp Op(string node, long ms, params (string Field, string Value)[] fields) =>
        SyncOp.SetFields("Todo", Row, new HlcTimestamp(ms, 0, node),
            fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal));
}
