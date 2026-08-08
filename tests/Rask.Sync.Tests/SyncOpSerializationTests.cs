using System.Text.Json;

namespace Rask.Sync.Tests;

// The log is the source of truth and it lives in a bucket, so its wire form is a contract: it must round
// trip exactly, survive repeated hops without accumulating encoding, and stay readable to a person looking
// at an object to work out why a merge went the way it did.
public class SyncOpSerializationTests
{
    private static readonly Guid Row = new("7f3a2b91-0000-0000-0000-000000000001");

    private static string Write(SyncOp op) => JsonSerializer.Serialize(op, SyncJsonContext.Default.SyncOp);

    private static SyncOp Read(string json) => JsonSerializer.Deserialize(json, SyncJsonContext.Default.SyncOp)!;

    [Fact]
    public void Round_trips_a_set_op()
    {
        var op = SyncOp.SetFields("Todo", Row, new HlcTimestamp(1786000000000, 3, "node-a"),
            new Dictionary<string, string> { ["title"] = "\"Buy milk\"", ["done"] = "true" });

        var back = Read(Write(op));

        Assert.Equal(op.Entity, back.Entity);
        Assert.Equal(op.Id, back.Id);
        Assert.Equal(op.Stamp, back.Stamp);
        Assert.Equal("\"Buy milk\"", back.Set!["title"]);
        Assert.Equal("true", back.Set["done"]);
        Assert.False(back.Deleted);
    }

    [Fact]
    public void Round_trips_a_delete_op()
    {
        var op = SyncOp.Delete("Todo", Row, new HlcTimestamp(5, 1, "node-b"));

        var back = Read(Write(op));

        Assert.True(back.Deleted);
        Assert.Null(back.Set);
    }

    // The failure this prevents: quoting the values would make `true` into `"true"`, and every hop through
    // the log would add another layer of escaping until the value was unreadable and no longer equal to
    // itself.
    [Fact]
    public void Values_are_written_as_json_not_as_quoted_strings()
    {
        var op = SyncOp.SetFields("Todo", Row, new HlcTimestamp(1, 0, "n"),
            new Dictionary<string, string> { ["done"] = "true", ["count"] = "42" });

        var json = Write(op);

        Assert.Contains("\"done\":true", json);
        Assert.Contains("\"count\":42", json);
        Assert.DoesNotContain("\"done\":\"true\"", json);
    }

    [Fact]
    public void Repeated_round_trips_do_not_accumulate_encoding()
    {
        var op = SyncOp.SetFields("Todo", Row, new HlcTimestamp(1, 0, "n"),
            new Dictionary<string, string> { ["nested"] = "{\"a\":[1,2]}" });

        var once = Write(op);
        var thrice = Write(Read(Write(Read(once))));

        Assert.Equal(once, thrice);
    }

    // The stamp is a sortable string on the wire so keys can be ordered without parsing. An object would
    // still round trip and would quietly destroy that.
    [Fact]
    public void The_stamp_is_written_as_its_sortable_string()
    {
        var json = Write(SyncOp.Delete("Todo", Row, new HlcTimestamp(255, 16, "node-a")));

        Assert.Contains("\"t\":\"0000000000FF-0010-node-a\"", json);
    }

    [Fact]
    public void Field_names_are_short_so_the_log_stays_small()
    {
        var json = Write(SyncOp.SetFields("Todo", Row, new HlcTimestamp(1, 0, "n"),
            new Dictionary<string, string> { ["title"] = "\"t\"" }));

        Assert.Contains("\"e\":", json);
        Assert.Contains("\"id\":", json);
        Assert.Contains("\"t\":", json);
        Assert.Contains("\"set\":", json);
    }

    [Fact]
    public void A_set_op_omits_the_delete_flag_entirely()
    {
        var json = Write(SyncOp.SetFields("Todo", Row, new HlcTimestamp(1, 0, "n"),
            new Dictionary<string, string> { ["title"] = "\"t\"" }));

        Assert.DoesNotContain("\"d\":", json);
    }

    [Fact]
    public void A_batch_round_trips_and_replays_to_the_same_state()
    {
        SyncOp[] log =
        [
            SyncOp.SetFields("Todo", Row, new HlcTimestamp(100, 0, "a"),
                new Dictionary<string, string> { ["title"] = "\"one\"" }),
            SyncOp.SetFields("Todo", Row, new HlcTimestamp(200, 0, "b"),
                new Dictionary<string, string> { ["done"] = "true" }),
        ];

        var json = JsonSerializer.Serialize(log, SyncJsonContext.Default.SyncOpArray);
        var back = JsonSerializer.Deserialize(json, SyncJsonContext.Default.SyncOpArray)!;

        var direct = new SyncState();
        direct.Apply(log);
        var viaWire = new SyncState();
        viaWire.Apply(back);

        Assert.Equal(direct.Get("Todo", Row)!.Values, viaWire.Get("Todo", Row)!.Values);
    }

    [Fact]
    public void A_malformed_stamp_is_rejected_rather_than_silently_defaulted()
    {
        Assert.ThrowsAny<JsonException>(() =>
            Read("""{"e":"Todo","id":"7f3a2b91-0000-0000-0000-000000000001","t":"nope"}"""));
    }
}
