using System.Text.RegularExpressions;
using Entries = RaskEntriesRask_Core_Tests;
using Row = Rask.Core.Tests.Live.RemountRow;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// A KNOWN DEFECT, pinned as it is today — not the behaviour wanted. An unkeyed child is identified by its
// type and its ORDINAL AMONG ALL of the parent's entries (Component.Children.cs, GetOrCreateChild), so a
// sibling that comes or goes in front of it moves that ordinal and the child is built anew: unmounted,
// mounted again, whatever it held itself gone. #1215 fixed this for a child beside KEYED siblings of its own
// type, by counting among the unkeyed children of that type only; the same count for every type would fix
// this. The day it does, this test fails, which is the point of it.
//
// Every row prints "<id>:<instance number>", the number being handed out when it mounts.
public partial class ConditionalSiblingRemountTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_sibling_that_comes_and_goes_in_front_remounts_every_unkeyed_stateful_child_after_it_each_time()
    {
        Row.Mounts = 0;
        Row.Unmounts = 0;
        var noted = false;
        var page = new StubComponent(() => Div[noted ? P["note"] : null, Entries.RemountRow.Id(7), Span["x"], Entries.RemountRow.Id(8)]);
        var first = Pairs(page.RenderAsLiveRoot());

        noted = true;
        var withTheNote = Pairs(page.RenderAsLiveRoot());
        noted = false;
        var withoutIt = Pairs(page.RenderAsLiveRoot());

        Assert.Equal("7:1 8:2", first);
        Assert.Equal("7:3 8:4", withTheNote);
        Assert.Equal("7:5 8:6", withoutIt);
        Assert.Equal(6, Row.Mounts);
        Assert.Equal(4, Row.Unmounts);
    }

    private static string Pairs(string html) => string.Join(' ', PairPattern().Matches(html).Select(m => m.Value));

    [GeneratedRegex(@"\d+:\d+")]
    private static partial Regex PairPattern();
}

/// <summary>Prints the instance number it was handed when it mounted, and counts its mounts and unmounts.</summary>
public sealed partial class RemountRow : Component
{
    internal static int Mounts;
    internal static int Unmounts;

    private int _instance;

    public required int Id { get; set; }

    protected override Task OnMount()
    {
        _instance = ++Mounts;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        Unmounts++;
        return Task.CompletedTask;
    }

    protected override Component? Render() => I[$"{Id}:{_instance}"];
}
