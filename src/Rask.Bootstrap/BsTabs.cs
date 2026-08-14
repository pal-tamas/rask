namespace Rask.Bootstrap;

// One tab in a BsTabs control. OnSelect/OnSelectAsync are forwarded to the tab's nav button (so your
// handler, which sets the active key on your page, re-renders through the live runtime).
public sealed record BsTabItem(
    object Key,
    string Title,
    Component Content,
    Action? OnSelect = null,
    Func<Task>? OnSelectAsync = null,
    bool Disabled = false);

// A Bootstrap tabs control driven by Rask's live runtime (no JS). Active is the key of the selected
// tab; only the active pane is rendered. Set Pills for the .nav-pills look.
public sealed partial class BsTabs : BsBlock
{
    public IReadOnlyList<BsTabItem>? Tabs { get; set; }
    public object? Active { get; set; }
    public bool? Pills { get; set; }
    public bool? Fill { get; set; }

    protected override Component? Render()
    {
        var tabs = Tabs ?? [];
        var navItems = new List<Component>(tabs.Count);
        Component? pane = null;

        foreach (var tab in tabs)
        {
            var isActive = Equals(tab.Key, Active);
            var selected = new Dictionary<string, string?> { ["selected"] = isActive ? "true" : "false" };

            navItems.Add(Li.Class("nav-item").Role("presentation").Key(tab.Key)[
                Button
                    .Type("button")
                    .Role("tab")
                    .Aria(selected)
                    .Class(BsClass.Join("nav-link", isActive ? "active" : null, tab.Disabled ? "disabled" : null))
                    .OnClick(tab.OnSelect)
                    .OnClickAsync(tab.OnSelectAsync)[tab.Title]]);

            if (isActive)
            {
                pane = Div.Class("tab-pane show active").Role("tabpanel")[tab.Content];
            }
        }

        var navCls = BsClass.Join("nav",
            Pills is true ? "nav-pills" : "nav-tabs",
            Fill is true ? "nav-fill" : null,
            Class);

        return Div.Id(Id)[
            Ul.Class(navCls).Role("tablist")[navItems],
            Div.Class("tab-content")[pane]];
    }
}
