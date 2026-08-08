namespace Rask.Sync.Tests;

// The merge rules themselves, stated one at a time. The convergence tests prove ordering does not matter;
// these pin what the resulting value actually IS, which is the part a user experiences.
public class SyncStateMergeTests
{
    private static readonly Guid Row = new("7f3a2b91-0000-0000-0000-000000000001");

    // The whole reason ops carry changed fields rather than whole rows: two people editing different
    // fields of the same record offline should both keep their work. A whole-row op would drop one.
    [Fact]
    public void Edits_to_different_fields_of_one_row_all_survive()
    {
        var state = new SyncState();

        state.Apply(Op("a", 100, ("title", "\"Buy milk\"")));
        state.Apply(Op("b", 101, ("done", "true")));

        var row = state.Get("Todo", Row)!;
        Assert.Equal("\"Buy milk\"", row.Values["title"]);
        Assert.Equal("true", row.Values["done"]);
    }

    [Fact]
    public void The_higher_stamp_wins_the_same_field()
    {
        var state = new SyncState();

        state.Apply(Op("a", 100, ("title", "\"older\"")));
        state.Apply(Op("b", 200, ("title", "\"newer\"")));

        Assert.Equal("\"newer\"", state.Get("Todo", Row)!.Values["title"]);
    }

    [Fact]
    public void An_older_op_arriving_late_does_not_overwrite_a_newer_value()
    {
        var state = new SyncState();

        state.Apply(Op("b", 200, ("title", "\"newer\"")));
        state.Apply(Op("a", 100, ("title", "\"older\"")));

        Assert.Equal("\"newer\"", state.Get("Todo", Row)!.Values["title"]);
    }

    [Fact]
    public void A_fully_tied_stamp_is_decided_by_the_node_not_by_arrival()
    {
        var first = new SyncState();
        first.Apply(Op("node-a", 100, ("title", "\"A\"")));
        first.Apply(Op("node-b", 100, ("title", "\"B\"")));

        var second = new SyncState();
        second.Apply(Op("node-b", 100, ("title", "\"B\"")));
        second.Apply(Op("node-a", 100, ("title", "\"A\"")));

        Assert.Equal("\"B\"", first.Get("Todo", Row)!.Values["title"]);
        Assert.Equal("\"B\"", second.Get("Todo", Row)!.Values["title"]);
    }

    [Fact]
    public void A_deleted_row_reads_as_absent()
    {
        var state = new SyncState();

        state.Apply(Op("a", 100, ("title", "\"t\"")));
        state.Apply(Delete("b", 200));

        Assert.Null(state.Get("Todo", Row));
        Assert.True(state.IsDeleted("Todo", Row));
    }

    // Without a tombstone the row would come back the moment any older op was replayed — which is exactly
    // what a peer re-reading the log does.
    [Fact]
    public void An_older_edit_replayed_after_a_delete_does_not_resurrect_the_row()
    {
        var state = new SyncState();

        state.Apply(Delete("b", 200));
        state.Apply(Op("a", 100, ("title", "\"t\"")));

        Assert.Null(state.Get("Todo", Row));
    }

    // Deliberate, and the counterpart of the rule above: if you edit a record after I deleted it, and your
    // edit is genuinely later, your edit wins. It is also what makes delete and edit commute.
    [Fact]
    public void A_newer_edit_after_a_delete_brings_the_row_back()
    {
        var state = new SyncState();

        state.Apply(Delete("b", 200));
        state.Apply(Op("a", 300, ("title", "\"back\"")));

        Assert.Equal("\"back\"", state.Get("Todo", Row)!.Values["title"]);
        Assert.False(state.IsDeleted("Todo", Row));
    }

    [Fact]
    public void A_later_delete_wins_over_an_earlier_delete_without_changing_visibility()
    {
        var state = new SyncState();

        state.Apply(Delete("a", 100));
        state.Apply(Delete("b", 200));

        Assert.Null(state.Get("Todo", Row));
    }

    [Fact]
    public void Rows_of_other_entities_are_untouched()
    {
        var state = new SyncState();

        state.Apply(SyncOp.SetFields("Todo", Row, new HlcTimestamp(100, 0, "a"),
            new Dictionary<string, string> { ["title"] = "\"todo\"" }));
        state.Apply(SyncOp.SetFields("Note", Row, new HlcTimestamp(100, 0, "a"),
            new Dictionary<string, string> { ["title"] = "\"note\"" }));

        Assert.Equal("\"todo\"", state.Get("Todo", Row)!.Values["title"]);
        Assert.Equal("\"note\"", state.Get("Note", Row)!.Values["title"]);
    }

    [Fact]
    public void All_lists_only_visible_rows()
    {
        var state = new SyncState();
        var second = new Guid("7f3a2b91-0000-0000-0000-000000000002");

        state.Apply(Op("a", 100, ("title", "\"kept\"")));
        state.Apply(SyncOp.SetFields("Todo", second, new HlcTimestamp(100, 0, "a"),
            new Dictionary<string, string> { ["title"] = "\"gone\"" }));
        state.Apply(SyncOp.Delete("Todo", second, new HlcTimestamp(200, 0, "a")));

        var all = state.All("Todo");

        Assert.Single(all);
        Assert.Equal(Row, all[0].Id);
    }

    [Fact]
    public void LastModified_reports_the_newest_field_stamp()
    {
        var state = new SyncState();

        state.Apply(Op("a", 100, ("title", "\"t\"")));
        state.Apply(Op("b", 500, ("done", "true")));

        Assert.Equal(500, state.Get("Todo", Row)!.LastModified.PhysicalMs);
    }

    // Values are opaque raw JSON: the engine must move an object or an array through untouched, because it
    // has no idea what the application's types are and must not need to.
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("{\"nested\":[1,2,3]}")]
    [InlineData("[1,\"two\",null]")]
    public void Values_of_any_json_shape_survive_unchanged(string raw)
    {
        var state = new SyncState();

        state.Apply(Op("a", 100, ("value", raw)));

        Assert.Equal(raw, state.Get("Todo", Row)!.Values["value"]);
    }

    private static SyncOp Op(string node, long ms, params (string Field, string Value)[] fields) =>
        SyncOp.SetFields("Todo", Row, new HlcTimestamp(ms, 0, node),
            fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal));

    private static SyncOp Delete(string node, long ms) =>
        SyncOp.Delete("Todo", Row, new HlcTimestamp(ms, 0, node));
}
