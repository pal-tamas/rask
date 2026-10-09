using System.Text.RegularExpressions;
using Entries = RaskEntriesRask_Core_Tests;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it
#pragma warning disable RASK022 // rows without a Key beside rows with one are the subject of these tests

namespace Rask.Core.Tests.Live;

// #1215, first half. Once ONE child of a type carried a Key, its parent stopped reusing every child of that
// type by position — so the unkeyed ones beside it were built anew on each render: a new instance, new
// handler ids, and whatever the row held itself gone. An unkeyed child of a keyed type is identified by its
// ORDER among the unkeyed children of that type instead, which the keyed ones coming and going cannot move.
//
// Every row prints "<id>:<instance number>", the number being handed out when it mounts — a row that was
// built again shows a new one.
public partial class UnkeyedSiblingIdentityTests
{
    [Fact]
    public void An_unkeyed_row_beside_a_keyed_one_keeps_its_instance_across_renders()
    {
        var list = NewList(before: [1], unkeyed: [100]);
        var first = Pairs(list.RenderAsLiveRoot());

        var second = Pairs(list.RenderAsLiveRoot());
        var third = Pairs(list.RenderAsLiveRoot());

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(2, SiblingRow.Mounts);
        Assert.Equal(0, SiblingRow.Unmounts);
    }

    [Fact]
    public void An_unkeyed_row_written_before_the_keyed_ones_keeps_its_instance_across_renders()
    {
        var list = NewList(unkeyed: [100], after: [1, 2]);
        var first = Pairs(list.RenderAsLiveRoot());

        var second = Pairs(list.RenderAsLiveRoot());
        var third = Pairs(list.RenderAsLiveRoot());

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(3, SiblingRow.Mounts);
    }

    [Fact]
    public void Adding_removing_and_reordering_keyed_rows_leaves_the_unkeyed_row_alone()
    {
        var list = NewList(before: [1, 2], unkeyed: [100], after: [3]);
        var unkeyed = Pair(list.RenderAsLiveRoot(), 100);

        list.Before.Insert(0, 9);
        var afterAdding = list.RenderAsLiveRoot();
        list.After.Clear();
        list.Before.Remove(1);
        var afterRemoving = list.RenderAsLiveRoot();
        list.Before.Reverse();
        var afterReordering = list.RenderAsLiveRoot();

        Assert.Equal(unkeyed, Pair(afterAdding, 100));
        Assert.Equal(unkeyed, Pair(afterRemoving, 100));
        Assert.Equal(unkeyed, Pair(afterReordering, 100));
        Assert.Equal(5, SiblingRow.Mounts);
        Assert.Equal(2, SiblingRow.Unmounts);
    }

    [Fact]
    public void Several_unkeyed_rows_are_told_apart_by_their_order_among_themselves()
    {
        var list = NewList(before: [1], unkeyed: [100, 101, 102]);
        var first = list.RenderAsLiveRoot();

        list.Before.InsertRange(0, [5, 6]);
        list.After.Add(7);
        var grown = list.RenderAsLiveRoot();
        list.Before.Clear();
        var shrunk = list.RenderAsLiveRoot();

        foreach (var id in new[] { 100, 101, 102 })
        {
            Assert.Equal(Pair(first, id), Pair(grown, id));
            Assert.Equal(Pair(first, id), Pair(shrunk, id));
        }
    }

    [Fact]
    public void Removing_the_last_unkeyed_row_unmounts_only_that_one()
    {
        var list = NewList(before: [1], unkeyed: [100, 101], after: [2]);
        var first = list.RenderAsLiveRoot();

        list.Unkeyed.Remove(101);
        var shorter = list.RenderAsLiveRoot();

        Assert.Equal(Pair(first, 100), Pair(shorter, 100));
        Assert.Equal(Pair(first, 1), Pair(shorter, 1));
        Assert.Equal(Pair(first, 2), Pair(shorter, 2));
        Assert.Equal(1, SiblingRow.Unmounts);
    }

    [Fact]
    public void Adding_an_unkeyed_row_mounts_only_the_new_one()
    {
        var list = NewList(before: [1], unkeyed: [100], after: [2]);
        var first = list.RenderAsLiveRoot();

        list.Unkeyed.Add(101);
        var longer = list.RenderAsLiveRoot();

        Assert.Equal(Pair(first, 100), Pair(longer, 100));
        Assert.Equal("101:4", Pair(longer, 101));
        Assert.Equal(0, SiblingRow.Unmounts);
    }

    [Fact]
    public void Re_rendering_an_unchanged_mix_of_keyed_and_unkeyed_rows_constructs_none()
    {
        var list = NewList(before: [1, 2], unkeyed: [100, 101], after: [3]);
        list.RenderAsLiveRoot();
        list.RenderAsLiveRoot();
        SiblingRow.Constructed = 0;

        list.RenderAsLiveRoot();
        list.RenderAsLiveRoot();

        Assert.Equal(0, SiblingRow.Constructed);
    }

    [Fact]
    public void A_new_key_written_before_an_unkeyed_row_gets_a_row_that_never_mounted()
    {
        var list = NewList(before: [1], unkeyed: [100]);
        var first = list.RenderAsLiveRoot();
        list.RenderAsLiveRoot();

        list.Before.Insert(0, 7);
        var grown = list.RenderAsLiveRoot();

        Assert.Equal("7:3", Pair(grown, 7));
        Assert.Equal(Pair(first, 100), Pair(grown, 100));
        Assert.Equal(Pair(first, 1), Pair(grown, 1));
    }

    [Fact]
    public void Steps_written_before_a_key_never_reach_the_unkeyed_row_beside_it()
    {
        var list = NewList(before: [1, 2], unkeyed: [100], after: [3]);
        list.KeyLast = true;
        var first = list.RenderAsLiveRoot();

        list.Before.Reverse();
        list.Before.Insert(0, 8);
        var changed = list.RenderAsLiveRoot();

        Assert.Equal(Pair(first, 100), Pair(changed, 100));
        Assert.Equal(Pair(first, 1), Pair(changed, 1));
        Assert.Equal("8:5", Pair(changed, 8));
    }

    [Fact]
    public void A_type_that_gets_its_first_key_on_a_later_render_keeps_its_unkeyed_rows()
    {
        var list = NewList(unkeyed: [100, 101]);
        var first = list.RenderAsLiveRoot();
        list.RenderAsLiveRoot();

        list.Before.Add(1);
        list.After.Add(2);
        var keyed = list.RenderAsLiveRoot();
        var again = list.RenderAsLiveRoot();

        Assert.Equal(Pair(first, 100), Pair(keyed, 100));
        Assert.Equal(Pair(first, 101), Pair(keyed, 101));
        Assert.Equal(Pairs(keyed), Pairs(again));
        Assert.Equal(4, SiblingRow.Mounts);
    }

    [Fact]
    public void An_unkeyed_row_beside_a_keyed_one_keeps_the_handler_it_registered()
    {
        PressRow.Constructed = 0;
        var list = new PressList();
        list.RenderAsLiveRoot();
        var second = list.RenderAsLiveRoot();

        var third = list.RenderAsLiveRoot();

        Assert.Equal(second, third);
        Assert.Equal(2, PressRow.Constructed);
    }

    [Fact]
    public void An_unkeyed_row_written_inside_a_keyed_rows_step_keeps_its_instance_across_renders()
    {
        NestRow.Mounts = 0;
        NestRow.Unmounts = 0;
        var list = new NestedRows();
        var first = list.RenderAsLiveRoot();

        var second = list.RenderAsLiveRoot();
        var third = list.RenderAsLiveRoot();

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(3, NestRow.Mounts);
        Assert.Equal(0, NestRow.Unmounts);
    }

    private static MixedList NewList(int[]? before = null, int[]? unkeyed = null, int[]? after = null)
    {
        SiblingRow.Mounts = 0;
        SiblingRow.Unmounts = 0;
        SiblingRow.Constructed = 0;
        var list = new MixedList();
        list.Before.AddRange(before ?? []);
        list.Unkeyed.AddRange(unkeyed ?? []);
        list.After.AddRange(after ?? []);
        return list;
    }

    private static string Pairs(string html) => string.Join(' ', PairPattern().Matches(html).Select(m => m.Value));

    private static string Pair(string html, int id) =>
        PairPattern().Matches(html).Select(m => m.Value).Single(p => p.StartsWith($"{id}:", StringComparison.Ordinal));

    [GeneratedRegex(@"\d+:\d+")]
    private static partial Regex PairPattern();
}

/// <summary>Keyed rows, then unkeyed rows of the same type, then keyed rows again.</summary>
public sealed partial class MixedList : Component
{
    public List<int> Before { get; } = [];

    public List<int> Unkeyed { get; } = [];

    public List<int> After { get; } = [];

    public bool KeyLast { get; set; }

    protected override Component? Render() =>
        Div[Before.Select(Keyed), Unkeyed.Select(id => (Component)Entries.SiblingRow.Id(id)), After.Select(Keyed)];

    private Component Keyed(int id) =>
        KeyLast ? Entries.SiblingRow.Id(id).Key(id) : Entries.SiblingRow.Key(id).Id(id);
}

/// <summary>Prints the instance number it was handed when it mounted, and counts what happens to it.</summary>
public sealed partial class SiblingRow : Component
{
    internal static int Mounts;
    internal static int Unmounts;
    internal static int Constructed;

    private int _instance;

    public SiblingRow() => Constructed++;

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

/// <summary>A keyed row and an unkeyed one, each registering a click handler.</summary>
public sealed partial class PressList : Component
{
    protected override Component? Render() =>
        Div[Entries.PressRow.Key("kept").Label("a"), Entries.PressRow.Label("b")];
}

/// <summary>A row with a handler of its own — the id it registers under is in the markup.</summary>
public sealed partial class PressRow : Component
{
    internal static int Constructed;

    private int _presses;

    public PressRow() => Constructed++;

    public required string Label { get; set; }

    protected override Component? Render() => Button.OnClick(() => _presses++)[$"{Label} {_presses}"];
}

/// <summary>An unkeyed row built as the argument of a keyed row's step, whose Key comes after it.</summary>
public sealed partial class NestedRows : Component
{
    protected override Component? Render() =>
        Div[
            Entries.NestRow.Id(1).Inner(Entries.NestRow.Id(2)).Key("outer"),
            Entries.NestRow.Key("last").Id(3)
        ];
}

/// <summary>A row that can hold another row, printing the instance number it mounted with.</summary>
public sealed partial class NestRow : Component
{
    internal static int Mounts;
    internal static int Unmounts;

    private int _instance;

    public required int Id { get; set; }

    public Component? Inner { get; set; }

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

    protected override Component? Render() => I[$"{Id}:{_instance}", Inner];
}
