
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
// This exercises the production rask-morph.ts in-process on Jint with a stub DOM. The
// user-observable side used to be covered by PlaygroundExampleTests (.pg-code-host count after a run);
// the playground was removed, so this unit guard is now the only thing holding the invariant.
public sealed class MorphManagedGuardTests
{
    [Fact]
    public void A_managed_node_in_the_incoming_tree_is_not_duplicated_and_a_correctly_placed_marker_survives()
    {
        var root = ScriptFixture.Run("MorphManagedGuardFixture");

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
