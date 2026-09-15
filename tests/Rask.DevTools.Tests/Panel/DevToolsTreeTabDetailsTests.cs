using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The Tree tab's detail pane: what the selected component is and every prop it was given, with its type — and what an
///     island's details say.
/// </summary>
public sealed class DevToolsTreeTabDetailsTests
{
    private static DevToolsFeed Feed()
    {
        var row = new DevToolsComponentNode(3, "TaskRow", "t1",
            [
                new DescribedProp("Label", "System.String", "Tag the release", false),
                new DescribedProp("Items", "System.Collections.Generic.List<Shop.Features.Item>", "Count = 2", false),
                new DescribedProp("ApiToken", "System.String", "••••", true),
                new DescribedProp("Assignee", "System.String", null, false),
            ],
            [], At: "0|0|1");
        var chart = new DevToolsComponentNode(4, "SalesChart", null,
            [new DescribedProp("Year", "System.Int32", "2026", false)], [], At: "0|1|1", Badge: "React");
        var feed = new DevToolsFeed();
        feed.RecordTree(new DevToolsComponentNode(1, "App", null, [], [row, chart]));
        return feed;
    }

#pragma warning disable RASK014 // the tab rendered alone, the way the panel page would chain it, with a feed given by hand
    private static RenderedComponent Tab(long? reveal) => RaskTest.Render(new DevToolsTreeTab { Feed = Feed(), Reveal = reveal });
#pragma warning restore RASK014

    [Fact]
    public void With_nothing_selected_the_pane_says_how_to_fill_it()
    {
        Assert.Contains("Select a component to see its props.", Tab(null).Html);
    }

    [Fact]
    public void The_selected_components_props_are_listed_with_their_types_and_values()
    {
        var page = Tab(3);

        var details = page.Find("[data-rask-devtools-details]");
        Assert.Contains("TaskRow", details.TextContent);
        Assert.Contains("key t1", details.TextContent);
        var rows = page.FindAll("[data-rask-devtools-details] tbody tr")
            .Select(r => r.Children.Select(c => c.TextContent.Trim()).ToArray()).ToList();
        Assert.Equal(["Label", "String", "Tag the release"], rows[0]);
        Assert.Equal(["Items", "List<Item>", "Count = 2"], rows[1]);
        Assert.Equal("••••", rows[2][2]);
        Assert.Equal("null", rows[3][2]);
    }

    [Fact]
    public void An_island_says_it_is_one_and_carries_its_runtime_badge_on_its_row_and_in_the_pane()
    {
        var page = Tab(4);

        var details = page.Find("[data-rask-devtools-details]");
        Assert.Contains("React", details.TextContent);
        Assert.Contains("An island", details.TextContent);
        Assert.Contains(page.FindAll("[role=\"treeitem\"]"), r => r.TextContent.Contains("SalesChart", StringComparison.Ordinal)
                                                                   && r.TextContent.Contains("React", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("System.String", "String")]
    [InlineData("System.Collections.Generic.Dictionary<System.String, Shop.Row>", "Dictionary<String, Row>")]
    [InlineData("Shop.Row[]", "Row[]")]
    [InlineData("System.Nullable<System.Int32>", "Nullable<Int32>")]
    public void A_type_is_shown_without_its_namespaces(string type, string shown)
    {
        Assert.Equal(shown, DevToolsTreeTab.ShortType(type));
    }
}
