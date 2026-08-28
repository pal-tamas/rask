
namespace Rask.Core.Tests.Live;

// Regression guard for the radio/checkbox `.checked` desync (E2E:
// StandaloneWasmExampleTests.Journey_WalksEveryPageAndUnusualActivity, the Forms
// guide's radio-group step).
//
// Symptom: after the user clicked a radio, a re-render the server computed BEFORE
// the change reached it landed afterwards; both client apply paths (the full morph
// in rask-morph.ts and the diff codec's syncFormProperty in rask-dom.ts) set
// `.checked` unconditionally, reverting the click. Playwright then reported
// "Clicking the checkbox did not change its state". The `.value` property already
// had a pending-edit guard; `.checked` did not.
//
// Fix: raskNotePendingChecked records the pre-click checked (the `checked`
// attribute a native click leaves untouched) on dispatch — for a radio, the whole
// same-name group; raskShouldSuppressChecked suppresses any frame that still
// carries that stale state until an authoritative frame differs, then releases so
// server-driven changes win again.
//
// This exercises the production rask-morph.ts + rask-dom.ts in a Node subprocess
// with a stub DOM. Pairs with the WASM/Server E2E journeys.
public sealed class MorphCheckedGuardTests
{
    [Fact]
    public void Checked_StaleRender_DoesNotClobberJustClickedRadioOrCheckbox_ThenReleases()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a
        // failure: node is not required to build or test Rask, and the browser-observable
        // half of this behaviour is covered by an E2E test.
        var result = NodeFixture.Run("MorphCheckedGuardFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        bool Get(string name) => root.GetProperty(name).GetBoolean();

        // Diff codec: the lagging RemoveAttribute-checked op must NOT unset the radio
        // the user just clicked; the SetAttribute echo then applies.
        Assert.True(Get("s1AfterStale"), "stale diff op reverted the clicked radio");
        Assert.True(Get("s1AfterEcho"), "echo diff op didn't apply");

        // Full morph, radio group: a stale frame must neither re-check the previously
        // selected radio (which would natively uncheck the new one) nor unset the new one.
        Assert.False(Get("s2FreeAfterStale"), "stale frame re-checked the old radio");
        Assert.True(Get("s2ProAfterStale"), "stale frame reverted the clicked radio");
        // The echo applies and releases both guards.
        Assert.False(Get("s2FreeAfterEcho"), "echo didn't unset the old radio");
        Assert.True(Get("s2ProAfterEcho"), "echo didn't apply to the clicked radio");
        // The guard released — a later server-driven change is not pinned.
        Assert.False(Get("s2ProAfterLater"), "guard pinned the value after the echo");

        // Full morph, lone checkbox: stale revert suppressed, echo applies.
        Assert.True(Get("s3AfterStale"), "stale frame reverted the checkbox");
        Assert.True(Get("s3AfterEcho"), "checkbox echo didn't apply");
    }


}
