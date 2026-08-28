
namespace Rask.Core.Tests.Live;

// Regression guard for the <select> desync (#588) — the third form property, and the only one that had
// no lagging-frame guard.
//
// Symptom: the user picks an option (the browser flips that option's `selected` PROPERTY and leaves
// every `selected` ATTRIBUTE where the server put it), then a re-render the server computed BEFORE the
// pick reached it lands. The diff codec's syncFormProperty set `.selected` unconditionally, so the box
// snapped back to the old option until the echo arrived. The focus guard in morph() doesn't help, for
// the same reason it doesn't help a date input: a select commits on change, so focus has moved on by
// the time the lagging frame lands (see MorphValueGuardTests).
//
// Fix: raskNotePendingSelected records the pre-pick `selected` attribute of EVERY option on dispatch —
// the whole select, exactly as the checked guard records the whole radio group, because a stale frame
// re-selecting the previously chosen option natively deselects the new one. raskShouldSuppressSelected
// then suppresses any frame still carrying that state until an authoritative one differs and releases.
//
// Second half: applying a selection through the SELECT's index rather than the option's own property,
// so one write moves the whole group instead of leaving a single-select momentarily showing its first
// option between a remove-op and a set-op.
//
// Exercises the production rask-morph.ts + rask-dom.ts in a Node subprocess with a stub DOM, alongside
// MorphCheckedGuardTests. Pairs with the WASM/Server E2E journeys, which cover the user-visible side.
public sealed class MorphSelectedGuardTests
{
    [Fact]
    public void Selected_StaleRender_DoesNotClobberTheJustPickedOption_ThenReleases()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a
        // failure: node is not required to build or test Rask, and the browser-observable
        // half of this behaviour is covered by an E2E test.
        var result = NodeFixture.Run("MorphSelectedGuardFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        bool Get(string name) => root.GetProperty(name).GetBoolean();

        // THE regression assertion. A lagging frame must neither re-select the option the server still
        // thinks is chosen (which natively deselects the new one) nor clear the one the user picked.
        Assert.False(Get("s1AfterStaleA"), "stale frame re-selected the old option");
        Assert.True(Get("s1AfterStaleB"), "stale frame reverted the user's pick");

        // The authoritative echo applies and releases both guards.
        Assert.False(Get("s1AfterEchoA"), "echo didn't clear the old option");
        Assert.True(Get("s1AfterEchoB"), "echo didn't apply to the picked option");

        // Released — a later server-driven change is not pinned by the guard.
        Assert.True(Get("s1AfterLaterA"), "guard pinned the selection after the echo");
        Assert.False(Get("s1AfterLaterB"), "moving the selection left the old option selected");

        // A select nobody has touched has no guard at all, so ordinary server-driven selection is
        // untouched by any of this.
        Assert.False(Get("s2AfterServerA"), "server-driven deselect didn't apply");
        Assert.True(Get("s2AfterServerB"), "server-driven select didn't apply");

        // Selecting moves the whole group in one write, rather than depending on a sibling's op also
        // arriving (and surviving the guard) to clear the option that was on.
        Assert.True(Get("s3OnlyCSelected"), "selecting one option didn't clear its siblings");

        // ...except on a multi-select, where several options are legitimately on at once.
        Assert.True(Get("s4BothSelected"), "a multi-select lost an already-selected option");
    }


}
