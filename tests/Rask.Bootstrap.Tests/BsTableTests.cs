using System.Text.RegularExpressions;
using Rask.Core;

namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsTable. It had no coverage of its own until the scroll/sticky/aria surface
// landed, so the style toggles are pinned here too: BsDataGrid renders through BsTable, and the CLI's
// scaffolded list screens emit it verbatim, so a change in this markup is felt in both.
public partial class BsTableTests : global::Rask.Core.RaskMarkup
{
    // `new`: hides the <body> tag entry the markup host brings in (CS0108).
    private static new Component Body() => Tbody[Tr[Td["cell"]]];

    [Fact]
    public void RendersABareTable_WhenNothingIsSet()
    {
        // Responsive defaults to unset here (BsDataGrid is what defaults it to true), so no wrapper.
        var html = BsTable[Body()].ToHtml();

        Assert.StartsWith("<table class=\"table\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("table-responsive", html, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleToggles_MapToTheirBootstrapClasses()
    {
        var html = BsTable
            .Striped(true)
            .StripedColumns(true)
            .Bordered(true)
            .Hover(true)
            .Small(true)
            .Color(BsColor.Dark)[Body()].ToHtml();

        Assert.Contains("table-striped", html, StringComparison.Ordinal);
        Assert.Contains("table-striped-columns", html, StringComparison.Ordinal);
        Assert.Contains("table-bordered", html, StringComparison.Ordinal);
        Assert.Contains("table-hover", html, StringComparison.Ordinal);
        Assert.Contains("table-sm", html, StringComparison.Ordinal);
        Assert.Contains("table-dark", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Responsive_WrapsTheTable()
    {
        var html = BsTable.Responsive(true)[Body()].ToHtml();

        Assert.StartsWith("<div class=\"table-responsive\"><table class=\"table\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void IdAndClass_LandOnTheTable_NotTheWrapper()
    {
        // Documented contract (docs/data-grid.md): Id and Class address the <table>. A second place for them
        // to land is exactly why MaxHeight reuses this wrapper instead of adding one of its own.
        var html = BsTable.Id("t1").Class("mb-0").Responsive(true)[Body()].ToHtml();

        Assert.Contains("<div class=\"table-responsive\"><table id=\"t1\" class=\"table mb-0\">", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Aria_LandsOnTheTable_NotTheWrapper()
    {
        // The point of the passthrough: aria-busy has to sit on the content being refetched. On the wrapper it
        // would also enclose any live region rendered beside the table, which defers announcements.
        var html = BsTable.Responsive(true).Aria(new Dictionary<string, string?> { ["busy"] = "true" })[Body()]
            .ToHtml();

        Assert.Contains("<table class=\"table\" aria-busy=\"true\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void MaxHeight_BoundsTheWrapper_SoTheBodyScrolls()
    {
        var html = BsTable.Responsive(true).MaxHeight("400px")[Body()].ToHtml();

        Assert.StartsWith("<div class=\"table-responsive\" style=\"max-height:400px\">", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MaxHeight_ImpliesTheWrapper_EvenWhenResponsiveIsOff()
    {
        // Without a scroll container the height would just clip the rows, which is never what was meant.
        var html = BsTable.MaxHeight("60vh")[Body()].ToHtml();

        Assert.StartsWith("<div class=\"table-responsive\" style=\"max-height:60vh\">", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MaxHeight_AndResponsive_ShareOneWrapper()
    {
        // Two nested .table-responsive divs would give the sticky header the wrong scroll container to
        // resolve against — the inner one, which has no bounded height.
        var html = BsTable.Responsive(true).MaxHeight("400px")[Body()].ToHtml();

        Assert.Single(Regex.Matches(html, "table-responsive"));
    }

    [Fact]
    public void StickyHeader_AddsTheClass_ToTheTable()
    {
        // The class is inert on its own; the .bs-table-sticky rule needs the bounded container MaxHeight
        // renders. Pinning them together here is what documents the pairing in code.
        var html = BsTable.StickyHeader(true).MaxHeight("400px")[Body()].ToHtml();

        Assert.Contains("<table class=\"table bs-table-sticky\">", html, StringComparison.Ordinal);
        Assert.Contains("style=\"max-height:400px\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPropertyStaysOptional_SoNoChainStepBecomesMandatory()
    {
        // BsTable ships in a published package. Ordering no longer matters — a chain names every step it
        // takes — but a property turning non-nullable with no member initializer (RASK001) does: it becomes
        // a step every existing caller must now take, which is a source break in a minor release.
        //
        // Read from the published attribute rather than from the property types, because that is the only
        // place the answer survives metadata: a member initializer compiles into the constructor, so
        // `string Color` and `string Color = ""` are the same symbol from here.
        var published = typeof(BsTable).Assembly
            .GetCustomAttributes(typeof(RaskRequiredPropertiesAttribute), false)
            .Cast<RaskRequiredPropertiesAttribute>()
            .Where(a => a.Component == "Rask.Bootstrap.BsTable")
            .SelectMany(a => a.Properties)
            .ToList();

        Assert.Empty(published);

        // The surface itself, so a rename or a removal is still caught.
        Assert.Equal(
            ["Aria", "Bordered", "Borderless", "Class", "Color", "Hover", "Id", "MaxHeight", "Responsive",
             "Small", "StickyHeader", "Striped", "StripedColumns", "Style"],
            typeof(BsTable).GetProperties()
                .Where(p => p.GetIndexParameters().Length == 0 && p.DeclaringType != typeof(Component))
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal));
    }
}
