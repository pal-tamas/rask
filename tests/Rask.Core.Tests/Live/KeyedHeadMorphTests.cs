namespace Rask.Core.Tests.Live;

// Regression guard for the keyed <head> reconciliation crash on WASM static-host
// hydration (E2E: StandaloneWasmExampleTests.Journey_WalksEveryPageAndUnusualActivity).
//
// Symptom: a WASM app served by a plain static host rendered a blank page; the .NET
// runtime booted and applied its first render, then the second morph threw
// "insertBefore ... reference node is not a child" in _raskMoveBefore.
//
// Cause: the App's <head> carries a keyed scoped-bundle <link> (data-rask-key="rsk-css"),
// which promotes the whole <head> to keyed reconciliation. It hydrates against the SDK
// index.html <head> (<base> + importmap <script> + <title>, none keyed); those from-side
// nodes don't match the new tree by node name and get removed. The keyed loop's `anchor`
// still pointed at a removed node, so the next insert referenced a node no longer in the
// parent. On the Server the <head> is fully Rask-rendered (no SDK nodes), so it never hit
// this.
//
// Fix: advance `anchor` past a from-node before removing it (the node-name-mismatch branch).
//
// This exercises the production rask-morph.js in a Node subprocess with a stub DOM whose
// insertBefore throws exactly like a browser. Pairs with the StandaloneWasm E2E.
public sealed class KeyedHeadMorphTests
{
    [Fact]
    public void KeyedHeadMorph_AgainstSdkInjectedHead_DoesNotThrow_AndConverges()
    {
        using var doc = NodeFixture.Run("tests/Rask.Core.Tests/Live/KeyedHeadMorphFixture.mjs", "src/Rask.Core/Resources/rask-morph.js");
        if (doc is null)
        {
            return;
        }

        var root = doc.RootElement;

        // The keyed reconciliation must NOT throw — this is the assertion that fails pre-fix
        // (insertBefore against the stale anchor pointing at the removed <base>).
        Assert.False(root.GetProperty("threw").GetBoolean(),
            $"Keyed head morph threw: {root.GetProperty("error").GetString()}");

        // And it must converge the from-head to the App's head: <title> then the keyed <link>,
        // with the SDK-injected <base>/<script> removed.
        var children = root.GetProperty("children").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "TITLE", "LINK" }, children);
    }
}
