using System.Text;

namespace Rask.Sync.Tests;

// The three properties the whole design rests on: replaying a log is order-independent, idempotent and
// convergent. Everything else about syncing over object storage — no coordination, no server, no need to
// remember what you already sent, no assumption that peers deliver in order — is downstream of these.
//
// They are asserted by brute force rather than by example. A hand-picked ordering proves that ordering
// works; running EVERY ordering proves that order does not matter, which is the actual claim. The op sets
// below are small enough to permute exhaustively and chosen to cover the interesting interactions:
// same-field races between nodes, different-field edits on one row, and deletes racing edits both ways.
public class SyncStateConvergenceTests
{
    private static readonly Guid Row = new("7f3a2b91-0000-0000-0000-000000000001");
    private static readonly Guid Other = new("7f3a2b91-0000-0000-0000-000000000002");

    public static TheoryData<string, SyncOp[]> LogsToPermute() => new()
    {
        {
            "same field, two nodes, one millisecond apart",
            [
                Op("a", 100, "title", "\"from A\""),
                Op("b", 101, "title", "\"from B\""),
            ]
        },
        {
            "different fields of one row from two nodes",
            [
                Op("a", 100, "title", "\"from A\""),
                Op("b", 100, "done", "true"),
                Op("a", 102, "notes", "\"n\""),
            ]
        },
        {
            "delete racing an edit",
            [
                Op("a", 100, "title", "\"from A\""),
                Delete("b", 101),
                Op("a", 102, "title", "\"after the delete\""),
            ]
        },
        {
            "edit racing a delete the other way",
            [
                Op("a", 100, "title", "\"from A\""),
                Op("b", 101, "title", "\"from B\""),
                Delete("a", 102),
            ]
        },
        {
            "two rows, interleaved, with a tie on the clock",
            [
                Op("a", 100, "title", "\"A one\""),
                Op("b", 100, "title", "\"B one\""),
                Op("a", 100, "title", "\"A two\"", Other),
                Delete("b", 100, Other),
            ]
        },
        {
            "same stamp delivered by two different fields",
            [
                Op("a", 100, "title", "\"t\""),
                Op("a", 100, "done", "true"),
                Op("b", 99, "title", "\"older\""),
            ]
        },
    };

    [Theory]
    [MemberData(nameof(LogsToPermute))]
    public void Every_ordering_of_a_log_converges_on_the_same_state(string because, SyncOp[] log)
    {
        var expected = Snapshot(Replay(log), log);

        foreach (var ordering in Permutations(log))
        {
            Assert.Equal(expected, Snapshot(Replay(ordering), log));
        }

        Assert.False(string.IsNullOrEmpty(because));
    }

    // A peer re-listing objects it already read, or a client retrying an upload it is not sure landed, both
    // deliver ops twice. If that were not free, every client would need to track what it had already seen —
    // which is a coordination problem, and coordination is the thing there is no server to do.
    [Theory]
    [MemberData(nameof(LogsToPermute))]
    public void Replaying_a_log_twice_changes_nothing(string because, SyncOp[] log)
    {
        var once = Replay(log);
        var twice = Replay([.. log, .. log]);

        Assert.Equal(Snapshot(once, log), Snapshot(twice, log));
        Assert.False(string.IsNullOrEmpty(because));
    }

    [Theory]
    [MemberData(nameof(LogsToPermute))]
    public void Duplicates_interleaved_anywhere_change_nothing(string because, SyncOp[] log)
    {
        var expected = Snapshot(Replay(log), log);

        // Duplicate each op in place, so the repeat arrives immediately rather than at the end.
        var interleaved = log.SelectMany(op => new[] { op, op }).ToArray();

        Assert.Equal(expected, Snapshot(Replay(interleaved), log));
        Assert.False(string.IsNullOrEmpty(because));
    }

    // Two replicas that saw the same ops by different routes must agree. This is the property that lets one
    // device upload to a bucket and another download in whatever order listing happens to return.
    [Theory]
    [MemberData(nameof(LogsToPermute))]
    public void Two_replicas_fed_in_opposite_orders_agree(string because, SyncOp[] log)
    {
        var forward = Replay(log);
        var backward = Replay(log.Reverse().ToArray());

        Assert.Equal(Snapshot(forward, log), Snapshot(backward, log));
        Assert.False(string.IsNullOrEmpty(because));
    }

    // A partial sync is the normal case: a peer has uploaded three of its five ops when the tab closes.
    // Applying any prefix must still converge once the rest arrives.
    [Theory]
    [MemberData(nameof(LogsToPermute))]
    public void A_partial_log_finished_later_lands_where_the_whole_log_would(string because, SyncOp[] log)
    {
        var whole = Snapshot(Replay(log), log);

        for (var split = 0; split <= log.Length; split++)
        {
            var state = new SyncState();
            state.Apply(log.Take(split));
            state.Apply(log.Skip(split));

            Assert.Equal(whole, Snapshot(state, log));
        }

        Assert.False(string.IsNullOrEmpty(because));
    }

    private static SyncState Replay(IEnumerable<SyncOp> ops)
    {
        var state = new SyncState();
        state.Apply(ops);
        return state;
    }

    // A canonical rendering of everything the log touched, so two states can be compared as text and a
    // failure names the row and field that diverged.
    private static string Snapshot(SyncState state, IEnumerable<SyncOp> log)
    {
        var builder = new StringBuilder();

        foreach (var (entity, id) in log.Select(o => (o.Entity, o.Id)).Distinct().OrderBy(k => k.Item2))
        {
            var row = state.Get(entity, id);
            if (row is null)
            {
                builder.Append(entity).Append('/').Append(id).Append(" = <deleted or absent>\n");
                continue;
            }

            builder.Append(entity).Append('/').Append(id).Append(" = ");
            foreach (var (field, value) in row.Values.OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                builder.Append(field).Append('=').Append(value).Append(' ');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static IEnumerable<SyncOp[]> Permutations(SyncOp[] ops)
    {
        if (ops.Length <= 1)
        {
            yield return ops;
            yield break;
        }

        for (var i = 0; i < ops.Length; i++)
        {
            var rest = ops.Where((_, index) => index != i).ToArray();
            foreach (var tail in Permutations(rest))
            {
                yield return [ops[i], .. tail];
            }
        }
    }

    private static SyncOp Op(string node, long ms, string field, string value, Guid? id = null) =>
        SyncOp.SetFields("Todo", id ?? Row, new HlcTimestamp(ms, 0, node),
            new Dictionary<string, string> { [field] = value });

    private static SyncOp Delete(string node, long ms, Guid? id = null) =>
        SyncOp.Delete("Todo", id ?? Row, new HlcTimestamp(ms, 0, node));
}
