using System.Text.RegularExpressions;
using Rask.Core.Diagnostics;
using Entries = RaskEntriesRask_Core_Tests;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// #1215, second half. A component's key is looked up among everything its OWNER writes, not among the
// children of the element it sits under — so the same key under two lists was ONE entry, both rows claimed
// the one instance on the next render, and each chain's steps landed on it in turn: the first list showed
// the second list's row.
//
// In the ConsoleRedirect collection because the last test swaps the process-wide diagnostics sink.
[Collection("ConsoleRedirect")]
public partial class RepeatedKeyTests
{
    [Fact]
    public void Two_lists_under_one_owner_keep_their_own_rows_when_a_key_repeats()
    {
        var page = NewLists(["a"], ["a"]);
        var first = page.RenderAsLiveRoot();

        var second = page.RenderAsLiveRoot();
        var third = page.RenderAsLiveRoot();

        Assert.Equal(
            "<div><ul><i data-rask-key=\"a\">0a:1</i></ul><ol><i data-rask-key=\"a\">1a:2</i></ol><p></p></div>",
            first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(2, ScopedRow.Mounts);
    }

    [Fact]
    public void Three_lists_under_one_owner_keep_their_own_rows_when_a_key_repeats()
    {
        var page = NewLists(["a", "b"], ["b", "a"], ["a"]);
        var first = Rows(page.RenderAsLiveRoot());

        var second = Rows(page.RenderAsLiveRoot());
        var third = Rows(page.RenderAsLiveRoot());

        Assert.Equal("0a:1 0b:2 1b:3 1a:4 2a:5", first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public void A_repeat_that_appears_in_a_later_list_mounts_a_row_of_its_own()
    {
        var page = NewLists(["a"], []);
        page.RenderAsLiveRoot();
        page.RenderAsLiveRoot();

        page.Lists[1].Add("a");
        var both = Rows(page.RenderAsLiveRoot());
        var again = Rows(page.RenderAsLiveRoot());

        Assert.Equal("0a:1 1a:2", both);
        Assert.Equal(both, again);
        Assert.Equal(0, ScopedRow.Unmounts);
    }

    [Fact]
    public void A_repeat_that_disappears_from_a_later_list_leaves_the_earlier_row_alone()
    {
        var page = NewLists(["a"], ["a"], ["a"]);
        page.RenderAsLiveRoot();

        page.Lists[2].Clear();
        var two = Rows(page.RenderAsLiveRoot());
        page.Lists[1].Clear();
        var one = Rows(page.RenderAsLiveRoot());

        Assert.Equal("0a:1 1a:2", two);
        Assert.Equal("0a:1", one);
        Assert.Equal(2, ScopedRow.Unmounts);
    }

    [Fact]
    public void Each_row_shows_its_own_values_whichever_of_the_repeats_comes_or_goes()
    {
        var page = NewLists(["a"], ["a"], ["a"]);
        page.RenderAsLiveRoot();

        page.Lists[0].Clear();
        var withoutFirst = Labels(page.RenderAsLiveRoot());
        page.Lists[0].Add("a");
        page.Lists[1].Clear();
        var withoutSecond = Labels(page.RenderAsLiveRoot());

        Assert.Equal("1a 2a", withoutFirst);
        Assert.Equal("0a 2a", withoutSecond);
    }

    [Fact]
    public void The_same_key_in_an_owner_and_in_a_component_it_renders_names_two_rows()
    {
        ScopedRow.Mounts = 0;
        var page = new OwnerAroundAnOwner();
        var first = Rows(page.RenderAsLiveRoot());

        var second = Rows(page.RenderAsLiveRoot());
        var third = Rows(page.RenderAsLiveRoot());

        Assert.Equal(3, ScopedRow.Mounts);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public void A_key_written_twice_in_one_render_is_reported_once_naming_the_owner_and_the_row()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.ResetReportOnceForTests();
        RaskDiagnostics.Sink = captured.Add;

        try
        {
            var page = NewLists(["a"], ["a"]);
            page.RenderAsLiveRoot();
            page.RenderAsLiveRoot();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }

        var report = Assert.Single(captured, e => e.Message.Contains(nameof(ScopedRow), StringComparison.Ordinal));
        Assert.Equal(RaskLogLevel.Warning, report.Level);
        Assert.Contains(nameof(ScopedLists), report.Message, StringComparison.Ordinal);
        Assert.Contains("\"a\"", report.Message, StringComparison.Ordinal);
    }

    private static ScopedLists NewLists(params string[][] lists)
    {
        ScopedRow.Mounts = 0;
        ScopedRow.Unmounts = 0;
        var page = new ScopedLists();
        for (var i = 0; i < lists.Length; i++)
        {
            page.Lists[i].AddRange(lists[i]);
        }

        return page;
    }

    private static string Rows(string html) => string.Join(' ', RowPattern().Matches(html).Select(m => m.Value));

    private static string Labels(string html) =>
        string.Join(' ', RowPattern().Matches(html).Select(m => m.Value.Split(':')[0]));

    [GeneratedRegex(@"\d[a-z]:\d+")]
    private static partial Regex RowPattern();
}

/// <summary>Three lists written by ONE owner, each row keyed by its item alone.</summary>
public sealed partial class ScopedLists : Component
{
    public List<string>[] Lists { get; } = [[], [], []];

    protected override Component? Render() =>
        Div[Ul[Rows(0)], Ol[Rows(1)], P[Rows(2)]];

    private IEnumerable<Component> Rows(int list) =>
        Lists[list].Select(key => (Component)Entries.ScopedRow.Key(key).Label($"{list}{key}"));
}

/// <summary>Writes a row keyed "a" and renders a component that writes one keyed "a" too.</summary>
public sealed partial class OwnerAroundAnOwner : Component
{
    protected override Component? Render() =>
        Div[Entries.ScopedRow.Key("a").Label("0a"), Entries.InnerOwner, Entries.InnerOwner];
}

/// <summary>An owner of its own: its key "a" is no business of whoever renders it.</summary>
public sealed partial class InnerOwner : Component
{
    protected override Component? Render() => P[Entries.ScopedRow.Key("a").Label("1a")];
}

/// <summary>Prints its label and the instance number it was handed when it mounted.</summary>
public sealed partial class ScopedRow : Component
{
    internal static int Mounts;
    internal static int Unmounts;

    private int _instance;

    public required string Label { get; set; }

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

    protected override Component? Render() => I[$"{Label}:{_instance}"];
}
