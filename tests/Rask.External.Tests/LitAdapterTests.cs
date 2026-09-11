using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

/// <summary>
///     The Lit island, driven below the C# boundary: the shipped runtime, the shipped adapter, and a real DOM, with a
///     plain custom element standing in for a Lit one.
/// </summary>
/// <remarks>
///     <para>
///         A package element's events are not properties. The build sends a handler for one as a prop named
///         <c>@sl-change</c>, and the adapter has to add it as a listener — and keep exactly one listener across
///         re-renders, replace it when C# does, and remove it when C# clears it. Every one of those is invisible to a
///         C# test and breaks as either a silent island or an event reaching C# twice.
///     </para>
///     <para>
///         Skips rather than fails when the fixture was not bundled: it borrows the happy-dom the Preact fixture
///         installs, so it is bundled exactly when that install succeeded — see the targets in the .csproj.
///     </para>
/// </remarks>
public sealed class LitAdapterTests
{
    private const string Fixture = "LitAdapterFixture";

    [SkippableFact]
    public void The_element_mounts_and_updates_with_the_props_csharp_rendered()
    {
        var doc = Run();

        Assert.Equal("Revenue", doc.GetProperty("labelOnMount").GetString());
        Assert.Equal("Costs", doc.GetProperty("labelAfterUpdate").GetString());
    }

    [SkippableFact]
    public void A_prop_csharp_stops_sending_falls_back_to_the_elements_own_default()
    {
        var doc = Run();

        // C# leaves an unset prop out rather than sending null. Assignment is all an element re-renders from, so without
        // the adapter restoring it the island would keep showing the last value C# ever set.
        Assert.Equal("danger", doc.GetProperty("toneOnMount").GetString());
        Assert.Equal("neutral", doc.GetProperty("toneAfterOmitted").GetString());
        Assert.Equal("danger", doc.GetProperty("toneAfterResent").GetString());
    }

    [SkippableFact]
    public void An_event_reaches_csharp_once_and_a_rerender_keeping_the_handler_adds_no_second_listener()
    {
        var doc = Run();

        Assert.Equal(["c7:3"], Ids(doc, "afterMount"));

        // One more, not two: the re-render revived the same function, and the adapter kept the listener it had.
        Assert.Equal(["c7:3", "c7:3"], Ids(doc, "afterSameHandler"));
    }

    [SkippableFact]
    public void A_replaced_handler_takes_over_and_a_cleared_one_stops_firing()
    {
        var doc = Run();

        Assert.Equal(["c7:3", "c7:3", "c7:4"], Ids(doc, "afterNewHandler"));
        Assert.Equal(["c7:3", "c7:3", "c7:4"], Ids(doc, "afterClear"));
    }

    [SkippableFact]
    public void Unmount_removes_the_element()
    {
        Assert.True(Run().GetProperty("islandEmptyAfterUnmount").GetBoolean(), "the island still had children after unmount");
    }

    [SkippableFact]
    public void Children_land_in_the_light_dom_and_a_keyed_child_keeps_its_identity_when_reordered()
    {
        // A Lit element projects its light DOM through <slot>, so that is where children go. Moving a keyed child rather
        // than re-creating it keeps its state; re-creating it would also disconnect and reconnect every other child.
        var doc = Run();

        Assert.Equal(["text:Total ", "fx-demo:first", "fx-demo:second"], Ids(doc, "childrenOnMount"));
        Assert.Equal(["text:Sum ", "fx-demo:second", "fx-demo:first!"], Ids(doc, "childrenAfterReorder"));
        Assert.True(doc.GetProperty("keyedElementKept").GetBoolean(), "the keyed child was re-created rather than moved");
        Assert.True(doc.GetProperty("textNodeKept").GetBoolean(), "the text child was re-created rather than updated");
    }

    [SkippableFact]
    public void Children_csharp_removes_are_taken_out_and_the_element_stays()
    {
        var doc = Run();

        Assert.Empty(Ids(doc, "childrenAfterRemoval"));
        Assert.True(doc.GetProperty("cardKept").GetBoolean(), "removing the children removed the element with them");
    }

    private static string[] Ids(JsonElement doc, string name) =>
        doc.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToArray();

    /// <summary>Runs the fixture, or skips with the reason it could not.</summary>
    private static JsonElement Run()
    {
        Skip.IfNot(
            File.Exists(NodeFixture.ScriptPath(Fixture)),
            $"'{NodeFixture.ScriptPath(Fixture)}' was not bundled: npm could not install happy-dom for the fixtures (no npm, "
            + "no network, or RaskPreactFixture=false), so the Lit adapter was not exercised on this machine.");

        var doc = NodeFixture.Run(Fixture);
        Skip.If(doc is null, "node is not on PATH, so the Lit adapter was not exercised.");

        return doc!.Value;
    }
}
