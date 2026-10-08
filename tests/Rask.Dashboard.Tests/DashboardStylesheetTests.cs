using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Rask.Core.Routing;
using Rask.Dashboard.Pages;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

// Asserts on what the console SHIPS and what its document carries, not on the build that produced it. The
// console has ONE stylesheet: its own, compiled by its own Tailwind build with the kit taken in as Tailwind
// source (Styles/dashboard.css), embedded and inlined by DashboardLayout. It inlined the kit's precompiled
// sheet before — which carries only the classes the KIT's sources name, so a utility written in a console
// page would have named a rule that exists nowhere.
public sealed partial class DashboardStylesheetTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_console_ships_one_stylesheet_of_its_own()
    {
        var sheets = typeof(DashboardLayout).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".css", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(["Rask.Dashboard.dashboard.css"], sheets);

        // Empty is DashboardStylesheet's documented behaviour when the resource is missing, and it would
        // render every page as unstyled HTML with nothing reporting it.
        Assert.NotEmpty(DashboardStylesheet.Css);
    }

    [Fact]
    public void The_consoles_sheet_carries_every_class_the_kits_own_sheet_does()
    {
        // The kit compiled IN, not linked beside: every class a kit component can write has its rule here.
        var missing = Classes(UiStylesheet.Css).Except(Classes(DashboardStylesheet.Css), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();

        Assert.True(Classes(UiStylesheet.Css).Count > 500, "the kit's sheet parsed to almost nothing.");
        Assert.True(missing.Length == 0, $"the console's sheet lacks {missing.Length} kit class(es): {string.Join(" ", missing.Take(40))}");
    }

    [Fact]
    public void The_consoles_sheet_carries_the_frames_reset_and_no_other()
    {
        // The console owns its document, and the reset it gets is the kit's — keyed to the attribute a document
        // drawn with the kit alone writes, so an application linking the kit is untouched. Tailwind's own
        // preflight would be a second one.
        Assert.Contains(":where([" + UiStylesheet.DocumentAttribute, DashboardStylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(":where([" + UiStylesheet.DocumentAttribute + "]) body", DashboardStylesheet.Css, StringComparison.Ordinal);
        Assert.DoesNotContain("html,:host", DashboardStylesheet.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consoles_sheet_ranks_utilities_above_the_kits_rules_and_its_corrections_above_both()
    {
        // The order statement in Styles/dashboard.css, read back out of the shipped bytes: the same one the
        // kit's own sheet and every app's sheet declare.
        var order = Regex.Matches(DashboardStylesheet.Css, @"@layer\s+([A-Za-z0-9_\-]+(?:\s*,\s*[A-Za-z0-9_\-]+)*)\s*[{;]")
            .SelectMany(m => m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(order.IndexOf("base") >= 0 && order.IndexOf("base") < order.IndexOf("utilities"), string.Join(", ", order));
        Assert.True(order.IndexOf("rask") >= 0 && order.IndexOf("rask") < order.IndexOf("utilities"), string.Join(", ", order));
        Assert.True(order.IndexOf("utilities") < order.IndexOf("rask-ui-corrections"), string.Join(", ", order));
    }

    [Fact]
    public async Task The_document_inlines_the_consoles_sheet_and_nothing_else()
    {
        // Development, because the fail-closed default denies everyone in Production and the chrome under
        // test sits behind that policy.
        await using var h = new DashboardHarness(environment: Environments.Development);
        h.Get<RouteState>().Path = "/_rask";

        var html = Page.RenderDocument(RaskDashboardShell, h.Services).Html;

        Assert.Single(Regex.Matches(html, "<style[\\s>]"));
        Assert.Contains(DashboardStylesheet.Css, html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"stylesheet\"", html, StringComparison.Ordinal);
    }

    // Every class with a rule of its own: `.name` in a selector.
    private static HashSet<string> Classes(string css) =>
        Regex.Matches(css, @"(?<![\w\\-])\.((?:\\.|[A-Za-z0-9_-])+)(?=[^{};]*\{)")
            .Select(m => Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1"))
            .Where(name => !char.IsDigit(name[0]))
            .ToHashSet(StringComparer.Ordinal);
}
