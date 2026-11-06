using System.Globalization;
using Entries = RaskEntriesRask_Core_Tests;

#pragma warning disable RASK014 // the test owns the page it renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// #1309. A component written INSIDE a keyed row, without a key of its own, was the n-th of its type its owner
// writes — so when a row above it went, every later row's component moved up one instance, and a press made a
// moment earlier on Bothman's delete ran on the instance that now drew Geidt's row: Geidt was deleted. An unkeyed
// component is now told apart by its order among the children of its NEAREST keyed ancestor, so position counts
// only inside one row.
public partial class RowScopedIdentityTests
{
    [Fact]
    public async Task A_late_delete_in_an_unkeyed_component_of_a_keyed_row_deletes_its_own_row_after_the_row_above_went()
    {
        var page = Page.Render(new RowScopedSuppliers());
        var bothman = page.HandlerIdFor("tr:has-text(\"Bothman\") button.remove", "click");

        await page.On("tr:has-text(\"Aminoff\") button.remove").Click();
        await page.Invoke(bothman);

        Assert.Equal(["Geidt"], page.Instance.Names);
    }

    [Fact]
    public async Task A_late_delete_reaches_its_own_row_after_the_rows_were_reordered()
    {
        var page = Page.Render(new RowScopedSuppliers());
        var aminoff = page.HandlerIdFor("tr:has-text(\"Aminoff\") button.remove", "click");

        page.Instance.Change(() => page.Instance.Names.Reverse());
        page.Render();
        await page.Invoke(aminoff);

        Assert.Equal(["Geidt", "Bothman"], page.Instance.Names);
    }

    [Fact]
    public async Task A_late_delete_reaches_its_own_row_when_the_key_is_written_after_the_rows_children()
    {
        var page = Page.Render(new RowScopedSuppliers { KeyLast = true });
        var bothman = page.HandlerIdFor("tr:has-text(\"Bothman\") button.remove", "click");

        await page.On("tr:has-text(\"Aminoff\") button.remove").Click();
        await page.Invoke(bothman);

        Assert.Equal(["Geidt"], page.Instance.Names);
    }

    [Fact]
    public async Task What_an_unkeyed_component_holds_itself_stays_with_its_row_when_a_row_above_goes()
    {
        var page = Page.Render(new RowScopedSuppliers());
        await page.On("tr:has-text(\"Bothman\") button.count").Click();
        await page.On("tr:has-text(\"Bothman\") button.count").Click();

        page.Instance.Change(() => page.Instance.Names.Remove("Aminoff"));
        page.Render();

        Assert.Equal("2", page.TextOf("tr:has-text(\"Bothman\") button.count"));
        Assert.Equal("0", page.TextOf("tr:has-text(\"Geidt\") button.count"));
    }

    [Fact]
    public async Task Two_components_of_one_type_in_a_row_are_each_their_own_by_their_order_in_the_row()
    {
        var page = Page.Render(new RowScopedSuppliers { TwoCounters = true });
        var second = page.FindAll("tr:has-text(\"Bothman\") button.count")[1].Attribute("data-rask-on-click")!;
        await page.Invoke(second);

        page.Instance.Change(() => page.Instance.Names.Remove("Aminoff"));
        page.Render();

        var counts = page.FindAll("tr:has-text(\"Bothman\") button.count").Select(button => button.TextContent).ToList();
        Assert.Equal(["0", "1"], counts);
    }

    [Fact]
    public async Task A_late_press_in_a_nested_keyed_row_acts_on_its_own_line_after_a_line_and_a_group_above_went()
    {
        var page = Page.Render(new RowScopedOrders());
        var late = page.HandlerIdFor("li[data-rask-key=\"b2\"] button.remove", "click");

        await page.On("li[data-rask-key=\"b1\"] button.remove").Click();
        page.Instance.Change(() => page.Instance.Groups.RemoveAt(0));
        page.Render();
        await page.Invoke(late);

        Assert.Equal(["b3"], page.Instance.Groups.Single().Lines);
    }

    [Fact]
    public async Task An_unkeyed_component_beside_the_keyed_rows_keeps_its_instance_as_the_rows_come_and_go()
    {
        var page = Page.Render(new RowScopedSuppliers { CounterBeside = true });
        await page.On("#beside button.count").Click();

        page.Instance.Change(() => page.Instance.Names.Remove("Aminoff"));
        page.Render();
        page.Instance.Change(() => page.Instance.Names.Insert(0, "Zeta"));
        page.Render();

        Assert.Equal("1", page.TextOf("#beside button.count"));
    }

    [Fact]
    public void Re_rendering_unchanged_keyed_rows_builds_no_component_again()
    {
        var page = Page.Render(new RowScopedSuppliers { TwoCounters = true });
        page.Instance.Change(() => { });
        page.Render();
        var built = RowScopedCounter.Built;

        page.Instance.Change(() => { });
        page.Render();
        page.Instance.Change(() => { });
        page.Render();

        Assert.Equal(built, RowScopedCounter.Built);
    }

    // No key anywhere: a component is still the n-th of its type its owner writes, so what it holds stays with its
    // place in the list, not with the item — exactly as before.
    [Fact]
    public async Task Without_a_key_a_component_keeps_its_place_in_the_list_rather_than_its_item()
    {
        var page = Page.Render(new RowScopedSuppliers { Unkeyed = true });
        await page.On("tr:has-text(\"Aminoff\") button.count").Click();

        page.Instance.Change(() => page.Instance.Names.Remove("Aminoff"));
        page.Render();

        Assert.Equal("1", page.TextOf("tr:has-text(\"Bothman\") button.count"));
    }
}

public sealed partial class RowScopedSuppliers : Component
{
    public List<string> Names { get; } = ["Aminoff", "Bothman", "Geidt"];

    public bool KeyLast { get; init; }

    public bool TwoCounters { get; init; }

    public bool CounterBeside { get; init; }

    public bool Unkeyed { get; init; }

    public void Change(Action edit)
    {
        edit();
        StateHasChanged();
    }

    protected override Component? Render() =>
        Div[
            CounterBeside ? Div.Id("beside")[Entries.RowScopedCounter] : null,
            Table[Tbody[Names.Select(Row)]]
        ];

    private Component Row(string name)
    {
        Component[] cells =
        [
            Td[name],
            Td[Entries.RowScopedCounter, TwoCounters ? Entries.RowScopedCounter : null],
            Td[Entries.RowScopedRemover.OnRemove(() => Names.Remove(name))]
        ];
        if (Unkeyed)
        {
            return Tr[cells];
        }

        return KeyLast ? Tr[cells].Key(name) : Tr.Key(name)[cells];
    }
}

public sealed partial class RowScopedOrders : Component
{
    public List<(string Name, List<string> Lines)> Groups { get; } =
    [
        ("a", ["a1", "a2"]),
        ("b", ["b1", "b2", "b3"]),
    ];

    public void Change(Action edit)
    {
        edit();
        StateHasChanged();
    }

    protected override Component? Render() =>
        Div[Groups.Select(group => Section.Key(group.Name)[
            H2[group.Name],
            Ul[group.Lines.Select(line => Li.Key(line)[line, Entries.RowScopedRemover.OnRemove(() => group.Lines.Remove(line))])]
        ])];
}

public sealed partial class RowScopedRemover : Component
{
    public Callback OnRemove { get; set; }

    protected override Component? Render() => Button.Class("remove").OnClick(() => OnRemove.Invoke())["Delete"];
}

public sealed partial class RowScopedCounter : Component
{
    private int _count;

    public RowScopedCounter() => Built++;

    public static int Built { get; private set; }

    protected override Component? Render() => Button.Class("count").OnClick(() => _count++)[_count.ToString(CultureInfo.InvariantCulture)];
}
