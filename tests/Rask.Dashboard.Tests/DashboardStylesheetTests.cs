using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Rask.Core.Routing;
using Rask.Dashboard.Pages;
using Rask.Testing;
using Rask.Ui;

namespace Rask.Dashboard.Tests;

// Asserts on what the console SHIPS and what its document carries, not on the build that produced it. The
// console used to compile a stylesheet of its own for the utilities its pages wrote; every page is drawn with
// Rask.Ui components now, so the kit's compiled sheet is the only one, inlined by DashboardLayout.
public sealed partial class DashboardStylesheetTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_console_ships_no_stylesheet_of_its_own()
    {
        // A resource left behind would mean a second sheet still builds — and a second vocabulary still
        // exists to be written against, where the kit's sheet could not see it.
        var resources = typeof(DashboardLayout).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(resources, name => name.EndsWith(".css", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_kits_stylesheet_ships()
    {
        // The only sheet the console has. Empty is UiStylesheet's documented behaviour when its resource is
        // missing, and it would render every page as unstyled HTML with nothing reporting it.
        Assert.NotEmpty(UiStylesheet.Css);
    }

    [Fact]
    public void The_kits_sheet_carries_the_console_frames_reset()
    {
        // The console owns its document and has no other sheet to take a reset from, so the frame's reset
        // travels in the kit's — keyed to UiShell's .rask-ops, so an application linking the kit is untouched.
        Assert.Contains(":where(.rask-ops", UiStylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("body:has(.rask-ops)", UiStylesheet.Css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_document_inlines_the_kits_sheet_and_nothing_else()
    {
        // Development, because the fail-closed default denies everyone in Production and the chrome under
        // test sits behind that policy.
        await using var h = new DashboardHarness(environment: Environments.Development);
        h.Get<RouteState>().Path = "/_rask";

        var html = RaskTest.RenderDocument(RaskDashboardShell, h.Services).Html;

        Assert.Single(Regex.Matches(html, "<style[\\s>]"));
        Assert.Contains(UiStylesheet.Css, html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"stylesheet\"", html, StringComparison.Ordinal);
    }
}
