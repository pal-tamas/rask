
namespace Rask.Core.Tests.Live;

// Regression guard for issue #419: the playground accumulated an empty .pg-code-host per
// full-HTML frame because data-rask-managed sat on a node the .NET side ALSO rendered.
//
// morph filters data-rask-managed nodes out of the existing (from) child list but, before the
// fix, not the incoming (to) list — so a marked node present in the payload had its from copy
// filtered out and its to copy left unpaired, appended fresh every morph (unbounded growth). The
// guard makes the to-side filter symmetric: a marked node in the incoming tree is always a misuse
// (a rendered node is part of the payload), so skipping it turns the mistake into a no-op.
//
// This exercises the production rask-morph.ts in a Node subprocess with a stub DOM. The
// user-observable side is covered by PlaygroundExampleTests (.pg-code-host count after a run).
public sealed class MorphManagedGuardTests
{
    [Fact]
    public void ManagedNodeInIncomingTree_IsNotDuplicated_AndCorrectlyPlacedMarkerSurvives()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a
        // failure: node is not required to build or test Rask, and the browser-observable
        // half of this behaviour is covered by an E2E test.
        var result = NodeFixture.Run("MorphManagedGuardFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        bool GetBool(string name) => root.GetProperty(name).GetBoolean();
        int GetInt(string name) => root.GetProperty(name).GetInt32();

        // The misuse (marker on the rendered host) is a no-op: after two morph frames the host is
        // still single, not duplicated, and the original Monaco DOM is untouched.
        Assert.Equal(1, GetInt("misuseHostCount"));
        Assert.True(GetBool("misuseMonacoKept"), "the original host / Monaco DOM was disturbed by the guard");

        // The correct placement (marker on Monaco's own child) survives a childless incoming host.
        Assert.Equal(1, GetInt("correctHostCount"));
        Assert.True(GetBool("correctMonacoKept"), "a correctly-marked library child was stripped by morph");
    }


}
