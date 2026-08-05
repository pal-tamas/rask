using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public sealed partial class PersistentStateTests
{
    private sealed record Filter(string Term, int Page);

    private sealed record Page(int Number);

    // The same key's record with a property renamed — what a deploy that edited a persisted type in place
    // looks like to a token written by the previous version.
    private sealed record RenamedFilter(string Query, int Page);

    [Fact]
    public void Round_trips_a_value()
    {
        var state = new PersistentState();

        state.Persist("filter", new Filter("boots", 2));

        Assert.True(state.TryGet<Filter>("filter", out var read));
        Assert.Equal(new Filter("boots", 2), read);
    }

    [Fact]
    public void A_key_that_was_never_written_is_a_miss()
    {
        var state = new PersistentState();

        Assert.False(state.TryGet<Filter>("filter", out var read));
        Assert.Null(read);
    }

    /// <summary>
    /// The whole point of the bag: a value written by one session is readable by the session rebuilt in its
    /// place, without either of them sharing an object.
    /// </summary>
    [Fact]
    public void A_restored_bag_reads_back_what_the_original_wrote()
    {
        var original = new PersistentState();
        original.Persist("filter", new Filter("boots", 2));
        original.Persist("tab", "reviews");

        var rebuilt = new PersistentState();
        rebuilt.Restore(original.Entries);

        Assert.True(rebuilt.TryGet<Filter>("filter", out var filter));
        Assert.Equal(new Filter("boots", 2), filter);
        Assert.True(rebuilt.TryGet<string>("tab", out var tab));
        Assert.Equal("reviews", tab);
    }

    /// <summary>
    /// A deploy can change the type behind a key while a token written by the previous version is still in
    /// a browser. JSON that cannot be read as the new type at all must come back as "no value" — never as
    /// an exception thrown out of user code during a reconnect, which would turn a cosmetic loss into a
    /// broken page.
    /// </summary>
    [Fact]
    public void A_value_whose_type_changed_between_deploys_reads_as_a_miss()
    {
        var before = new PersistentState();
        before.Persist("selection", "reviews");

        var after = new PersistentState();
        after.Restore(before.Entries);

        // The key used to hold a string; this version reads it as a record. Unreadable, so: a miss.
        Assert.False(after.TryGet<Page>("selection", out var read));
        Assert.Null(read);

        // And the session carries on — the miss is not sticky, and other keys are unaffected.
        after.Persist("selection", new Page(3));
        Assert.True(after.TryGet<Page>("selection", out var rewritten));
        Assert.Equal(new Page(3), rewritten);
    }

    /// <summary>
    /// The other half of the same story, and the one that bites quietly: a shape that is merely *different*
    /// rather than unreadable is not a miss. System.Text.Json fills what it can't find with defaults, so a
    /// renamed property comes back as a successfully-read object with a null in it. That is STJ's behaviour
    /// and not something to paper over here — but it is why the docs tell you to version a persisted type by
    /// changing its key rather than by editing it in place.
    /// </summary>
    [Fact]
    public void A_renamed_property_reads_back_with_a_default_not_a_miss()
    {
        var before = new PersistentState();
        before.Persist("filter", new Filter("boots", 2));

        var after = new PersistentState();
        after.Restore(before.Entries);

        Assert.True(after.TryGet<RenamedFilter>("filter", out var read));
        Assert.NotNull(read);
        Assert.Null(read!.Query);
        Assert.Equal(2, read.Page);
    }

    [Fact]
    public void Remove_and_clear_drop_values()
    {
        var state = new PersistentState();
        state.Persist("a", 1);
        state.Persist("b", 2);

        Assert.True(state.Remove("a"));
        Assert.False(state.Remove("a"));
        Assert.False(state.TryGet<int>("a", out _));
        Assert.True(state.TryGet<int>("b", out _));

        state.Clear();
        Assert.False(state.TryGet<int>("b", out _));
    }

    /// <summary>
    /// The handoff layer re-signs and re-sends a token only when the version moves, so an idle session must
    /// not tick — and a write that changes nothing observable still counts as a change.
    /// </summary>
    [Fact]
    public void Version_tracks_mutations_and_only_mutations()
    {
        var state = new PersistentState();
        Assert.Equal(0, state.Version);

        state.Persist("a", 1);
        var afterWrite = state.Version;
        Assert.True(afterWrite > 0);

        state.TryGet<int>("a", out _);
        Assert.Equal(afterWrite, state.Version);

        state.Remove("missing");
        Assert.Equal(afterWrite, state.Version);

        state.Remove("a");
        Assert.True(state.Version > afterWrite);
    }

    /// <summary>A restored bag starts at version 0 — it must not re-issue a token for state it was just handed.</summary>
    [Fact]
    public void Restore_resets_the_version()
    {
        var original = new PersistentState();
        original.Persist("a", 1);

        var rebuilt = new PersistentState();
        rebuilt.Restore(original.Entries);

        Assert.Equal(0, rebuilt.Version);
    }

    /// <summary>
    /// Over budget the write still lands — refusing it would lose state the app believes it stored, and
    /// throwing would turn a size budget into an exception at an arbitrary call site. What the session loses
    /// is its resumability, which degrades to the reload it would have had anyway.
    /// </summary>
    [Fact]
    public void Exceeding_the_budget_keeps_the_write_but_marks_the_session_unresumable()
    {
        var state = new PersistentState { MaxBytes = 128 };

        state.Persist("small", "x");
        Assert.False(state.Overflowed);

        state.Persist("big", new string('x', 512));

        Assert.True(state.Overflowed);
        Assert.True(state.TryGet<string>("big", out var read));
        Assert.Equal(new string('x', 512), read);
    }

    /// <summary>
    /// Once over budget the session stays unresumable for its lifetime. Recomputing it per write would let a
    /// page that briefly spikes flicker between resumable and not, so a reconnect's outcome would depend on
    /// which side of a transient write it landed.
    /// </summary>
    [Fact]
    public void Overflow_does_not_clear_when_the_bag_shrinks_again()
    {
        var state = new PersistentState { MaxBytes = 128 };
        state.Persist("big", new string('x', 512));
        Assert.True(state.Overflowed);

        state.Remove("big");

        Assert.True(state.Overflowed);
    }

    /// <summary>
    /// Keys ride the wire alongside their values. Without counting them, many tiny keys would pass a
    /// value-only budget while producing a token far larger than the budget allows.
    /// </summary>
    [Fact]
    public void The_budget_counts_keys_not_just_values()
    {
        var state = new PersistentState { MaxBytes = 64 };

        for (var i = 0; i < 20; i++)
        {
            // 16-char key, 1-byte value: values alone total 20 bytes, well under the budget.
            state.Persist(new string('k', 15) + i, 1);
        }

        Assert.True(state.Overflowed);
    }

    /// <summary>The overflow is reported to the developer, not swallowed — it explains a reload they'd otherwise chase.</summary>
    [Fact]
    public void Exceeding_the_budget_reports_a_diagnostic()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.ResetReportOnceForTests();
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            var state = new PersistentState { MaxBytes = 32 };
            state.Persist("big", new string('x', 256));
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }

        var warning = Assert.Single(captured);
        Assert.Equal(RaskLogLevel.Warning, warning.Level);
        Assert.Contains("resumable", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Overwriting_a_key_replaces_rather_than_accumulates()
    {
        var state = new PersistentState { MaxBytes = 512 };

        for (var i = 0; i < 50; i++)
        {
            state.Persist("same", new string('x', 100));
        }

        Assert.False(state.Overflowed);
        Assert.True(state.TryGet<string>("same", out var read));
        Assert.Equal(new string('x', 100), read);
    }

    [Fact]
    public void An_empty_key_is_rejected()
    {
        var state = new PersistentState();

        Assert.Throws<ArgumentException>(() => state.Persist("", 1));
        Assert.Throws<ArgumentNullException>(() => state.Persist(null!, 1));
    }

    /// <summary>The trim-/AOT-safe overloads must behave identically to the reflection ones.</summary>
    [Fact]
    public void The_typeinfo_overloads_round_trip_the_same_values()
    {
        var state = new PersistentState();
        JsonTypeInfo<Filter> typeInfo = TestJsonContext.Default.Filter;

        state.Persist("filter", new Filter("boots", 2), typeInfo);

        Assert.True(state.TryGet("filter", typeInfo, out var read));
        Assert.Equal(new Filter("boots", 2), read);

        // ...and interoperate with the reflection path, since both write the same web-defaults JSON.
        Assert.True(state.TryGet<Filter>("filter", out var viaReflection));
        Assert.Equal(new Filter("boots", 2), viaReflection);
    }

    /// <summary>Values are stored as UTF-8 JSON, which is what makes the handoff record buildable without a walk.</summary>
    [Fact]
    public void Entries_are_utf8_json_ready_for_the_wire()
    {
        var state = new PersistentState();
        state.Persist("tab", "reviews");

        var raw = Encoding.UTF8.GetString(state.Entries["tab"]);

        Assert.Equal("\"reviews\"", raw);
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(Filter))]
    private sealed partial class TestJsonContext : JsonSerializerContext;
}
