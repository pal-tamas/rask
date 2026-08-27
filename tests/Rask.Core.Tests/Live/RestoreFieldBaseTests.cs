using System.Text.Json;

namespace Rask.Core.Tests.Live;

// The merge base behind the redeploy form restore (#571).
//
// When a replacement server can't carry a session over, the page reloads and rask.js re-applies the
// fields the user had actually edited — but only when the replacement renders the SAME base the old
// server did. That comparison is what makes the restore a three-way merge rather than a guess about
// whose copy is newer, and it is only sound if the base is what the server had rendered BEFORE the user
// touched the field.
//
// It cannot be read off the DOM at reload time. morph() and applyDiff both sync the `value` ATTRIBUTE
// unconditionally — only the `.value` PROPERTY is guarded — so every echo of the user's own keystrokes
// rewrites it. Capture at first-dirty (raskNoteDirtyField) and the echo is harmless; capture late and
// base always equals the user's text, so every field compares unequal against a pristine replacement and
// nothing is ever restored. That failure is silent and would still pass a naive test, which is why the
// echo is reproduced explicitly below.
//
// Exercises the production rask-morph.ts in a Node subprocess with a stub DOM, alongside
// MorphValueGuardTests. The merge decision and the converge send live in rask.js — an IIFE that boots a
// WebSocket and is a host entry point that boots a socket against a live document — so those are covered by E2E instead.
public sealed class RestoreFieldBaseTests
{
    [Fact]
    public void DirtyFieldBase_IsCapturedBeforeTheEcho_AndDescribesTheControlNotTheElement()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a
        // failure: node is not required to build or test Rask, and the browser-observable
        // half of this behaviour is covered by an E2E test.
        var result = NodeFixture.Run("RestoreFieldBaseFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        // THE regression assertion. The server echoed the user's text back and the `value` attribute
        // duly became it — but the base still reads what the server had rendered before the edit. Read
        // the base late instead and these two are equal, and nothing is ever restored.
        Assert.Equal("", root.GetProperty("baseAfterEcho").GetString());
        Assert.Equal("hello", root.GetProperty("attributeAfterEcho").GetString());

        // Capture-once: the first edit owns the base, so continuing to type doesn't erode it either.
        Assert.Equal("", root.GetProperty("baseAfterSecondEdit").GetString());

        // An uncontrolled input (no `value` attribute at all — the state morph deliberately never
        // writes to) is not the same as one the server rendered empty, and must not be flattened into
        // it: the two want opposite treatment on the far side of the reload.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("uncontrolledBase").ValueKind);
        Assert.Equal("", root.GetProperty("controlledEmptyBase").GetString());

        // A radio's meaningful state is its group's selection. Per-element `checked` would let a
        // restore re-check the user's pick — natively un-checking the server's — while the bases still
        // compared equal. The group is same-name AND same form owner, so a second form's `plan` group
        // stays out of it.
        Assert.Equal("std", root.GetProperty("radioBase").GetString());
        Assert.Equal(2, root.GetProperty("radioGroupSize").GetInt32());

        // A textarea's server-rendered value is its text content, not a `value` attribute.
        Assert.Equal("first draft", root.GetProperty("textareaBase").GetString());

        // The composition that keeps the restore on screen: arming the existing value guard with the
        // replacement's pristine base means the server's first catch-up frame — computed before the
        // converge message can reach it — is suppressed instead of wiping the restored text.
        Assert.Equal("hello", root.GetProperty("afterPristineFrame").GetString());
        // The echo of our own converge differs from that pristine value, so it applies and releases.
        Assert.Equal("hello", root.GetProperty("afterConvergeEcho").GetString());
        // And a genuine later server change wins, so the restore doesn't pin the field forever.
        Assert.Equal("server", root.GetProperty("afterLaterServerChange").GetString());

        // A field the user never touched is not a candidate: whatever the replacement renders for it
        // may well be newer than what this page was holding.
        Assert.False(root.GetProperty("untouchedIsDirty").GetBoolean());
    }


}
