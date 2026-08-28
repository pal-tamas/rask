using System.Reflection;
using Rask.Dashboard.Pages;

namespace Rask.Dashboard.Tests;

// Asserts on the SHIPPED artifact, not on the build that produced it. The stylesheet is compiled by
// Tailwind during this package's build and embedded as a manifest resource; DashboardLayout reads it
// back and inlines it. Every link in that chain is silent when it breaks — a missing resource yields
// an unstyled console rather than an error (deliberately: an app should not fail to start because its
// dashboard has no CSS), and a Tailwind run that scanned the wrong directory yields a stylesheet that
// is present, valid, and missing every class the pages actually use.
public class DashboardStylesheetTests
{
    private static string Css()
    {
        var assembly = typeof(DashboardLayout).Assembly;
        using var stream = assembly.GetManifestResourceStream("Rask.Dashboard.dashboard.css");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_stylesheet_is_embedded_and_not_empty() => Assert.NotEmpty(Css());

    // Tailwind emits only the utilities it finds in the sources it scans, so a rule that scans nothing
    // still produces a valid, useless file. These are picked from the theme and from the pages, so a
    // scan that missed Pages/ or lost the @theme block fails here rather than in a screenshot.
    [Theory]
    [InlineData("--color-ops-bg")]       // the @theme block reached the output
    [InlineData("bg-ops-panel")]         // a themed utility, so the theme is wired to utilities
    [InlineData("text-ops-muted")]
    [InlineData("tabular-nums")]         // used by every counter on the console
    [InlineData("animate-spin")]         // the loading spinner, which ships no asset of its own
    public void The_stylesheet_carries_the_classes_the_pages_use(string fragment) =>
        Assert.Contains(fragment, Css(), StringComparison.Ordinal);

    // The console renders inside the host application's document, so its reset must not escape.
    [Fact]
    public void The_console_is_scoped_so_its_reset_cannot_reach_the_host_app() =>
        Assert.Contains("rask-ops", Css(), StringComparison.Ordinal);
}
