using Rask.Testing;

namespace Rask.Example.Site.Tests;

// The README and the docs need the architecture animation as a real file — GitHub cannot run a
// component. That file is not a second source: it is BAKED from FlowAnimation and compared here
// byte-for-byte, so the two can never drift. Re-bake after editing the component with:
//
//     RASK_BAKE_FLOW_SVG=1 dotnet test tests/Rask.Example.Site.Tests
//
// The last two tests are the ones that matter over time. A diagram of forty packages goes stale
// silently — the usual way it is found is somebody reading it a year later — so the map's coverage is
// asserted against the directories actually on disk rather than trusted.
public partial class FlowAnimationBakeTests : global::Rask.Core.RaskMarkup
{
    private const string BakeEnvironmentVariable = "RASK_BAKE_FLOW_SVG";

    [Fact]
    public void Baked_svg_matches_the_component()
    {
        var rendered = RaskTest.Render(() => FlowAnimation).Html;
        var path = BakedSvgPath();

        if (Environment.GetEnvironmentVariable(BakeEnvironmentVariable) is not (null or ""))
        {
            File.WriteAllText(path, rendered + "\n");
        }

        Assert.True(
            File.Exists(path),
            $"{path} is missing. Bake it with {BakeEnvironmentVariable}=1 dotnet test tests/Rask.Example.Site.Tests.");

        // Normalised on the trailing newline only: the file keeps one so it is a well-behaved text file
        // in git, while the renderer emits none.
        Assert.Equal(rendered, File.ReadAllText(path).TrimEnd('\n', '\r'));
    }

    // The properties the README asset depends on, asserted on the markup rather than on the file, so a
    // component edit that breaks one fails here instead of silently baking a broken asset.
    [Fact]
    public void Baked_svg_is_a_standalone_document()
    {
        var html = RaskTest.Render(() => FlowAnimation).Html;

        // Without the namespace the file is not a valid SVG document and will not render through <img>.
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", html, StringComparison.Ordinal);

        // Motion has to be escapable, and the base state is authored as the final frame so that turning
        // the animations off lands on the finished picture rather than a blank one.
        Assert.Contains("prefers-reduced-motion: reduce", html, StringComparison.Ordinal);

        // An <img>-loaded SVG cannot see the page's theme, only the OS's — so the standalone file has to
        // answer prefers-color-scheme itself or it is unreadable for half its readers.
        Assert.Contains("prefers-color-scheme: light", html, StringComparison.Ordinal);

        // An id in an inline SVG is global to the whole HTML document, so two copies of the diagram on
        // one page would collide. Markers, clip paths and gradients are all avoided for this reason.
        Assert.DoesNotContain(" id=\"", html, StringComparison.Ordinal);
    }

    // Every colour must carry a literal fallback: standalone, none of the site's custom properties are
    // defined, so a bare var(--color-accent) would paint nothing at all.
    [Theory]
    [InlineData("--color-ground")]
    [InlineData("--color-panel")]
    [InlineData("--color-panel-2")]
    [InlineData("--color-line")]
    [InlineData("--color-grid")]
    [InlineData("--color-ink")]
    [InlineData("--color-ink-soft")]
    [InlineData("--color-muted")]
    [InlineData("--color-accent")]
    [InlineData("--color-accent-ink")]
    [InlineData("--color-signal")]
    public void Every_theme_token_carries_a_literal_fallback(string token)
    {
        var html = RaskTest.Render(() => FlowAnimation).Html;

        // The token must be referenced at all — a rename in Styles/app.css that this file did not follow
        // would otherwise leave the diagram rendering on its fallbacks, silently off-theme.
        Assert.Contains($"var({token},", html, StringComparison.Ordinal);

        // …and never referenced without one.
        Assert.DoesNotContain($"var({token})", html, StringComparison.Ordinal);
    }

    // An SVG <style> inline in an HTML document is not scoped to the SVG — its rules apply to the whole
    // page. Every selector and keyframe therefore has to be prefixed, or the hero would restyle the site
    // around it.
    [Fact]
    public void Every_class_and_keyframe_is_prefixed()
    {
        var html = RaskTest.Render(() => FlowAnimation).Html;

        foreach (var selector in ClassSelectors(html))
        {
            Assert.StartsWith("rf-", selector, StringComparison.Ordinal);
        }

        foreach (var name in KeyframeNames(html))
        {
            Assert.StartsWith("rf-", name, StringComparison.Ordinal);
        }
    }

    // THE STALENESS GATE. Every project under src/ has to appear somewhere on the map. Enumerating the
    // directories that contain a .csproj rather than every directory is deliberate and does two jobs: it
    // is the definition of "a package", and it means leftover bin/obj-only directories from a deleted
    // project cannot fail this test.
    [Fact]
    public void Every_source_package_has_a_box_on_the_map()
    {
        var packages = Directory
            .EnumerateDirectories(SourceRoot())
            .Where(d => Directory.EnumerateFiles(d, "*.csproj").Any())
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var covered = global::Rask.Example.Site.FlowAnimation.CoveredPackages().ToHashSet(StringComparer.Ordinal);

        var undrawn = packages.Except(covered).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.True(
            undrawn.Length == 0,
            $"These src/ projects have no box in FlowAnimation: {string.Join(", ", undrawn)}. "
            + "Add them to FlowAnimation.Nodes.cs — a package the diagram does not show is a package "
            + "readers will not know exists.");

        // The other direction matters just as much: a box naming a package that was deleted is a lie the
        // diagram tells confidently.
        var phantom = covered.Except(packages).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.True(
            phantom.Length == 0,
            $"FlowAnimation draws packages that no longer exist under src/: {string.Join(", ", phantom)}.");
    }

    // The SPA lane advertises a framework list, and `rask new --template …` advertises another. They are
    // the same list, and this is what keeps them that way.
    [Fact]
    public void Every_spa_template_has_a_chip()
    {
        var advertised = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "src", "Rask.Cli", "Commands", "DoctorCommand.cs"));

        var chips = global::Rask.Example.Site.FlowAnimation.SpaTemplateChips();

        foreach (var chip in chips)
        {
            Assert.Contains(chip.ToLowerInvariant(), advertised, StringComparison.Ordinal);
        }

        // Read the pipe-separated list the CLI prints back out, so a template ADDED there without a chip
        // here fails too — the direction that would otherwise go unnoticed.
        const string Marker = "rask new --template ";
        var start = advertised.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "DoctorCommand no longer prints the template list this test reads.");

        var tail = advertised[(start + Marker.Length)..];
        var end = tail.IndexOfAny(['`', '"']);
        var advertisedNames = tail[..end]
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var name in advertisedNames)
        {
            Assert.Contains(chips, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---- reading the generated stylesheet back out ----
    //
    // Deliberately crude string scanning rather than a CSS parser: the point is to police what this one
    // generator emits, and a dependency would be a bigger risk than the shape it is checking.
    private static string StyleBlock(string html)
    {
        var open = html.IndexOf("<style>", StringComparison.Ordinal);
        Assert.True(open >= 0, "FlowAnimation no longer emits a <style> block.");
        var start = open + "<style>".Length;
        var end = html.IndexOf("</style>", start, StringComparison.Ordinal);
        Assert.True(end > start, "The <style> block is not closed.");
        return html[start..end];
    }

    private static IEnumerable<string> ClassSelectors(string html)
    {
        var style = StyleBlock(html);

        for (var i = 0; i < style.Length; i++)
        {
            // A '.' only opens a selector at the start of one — otherwise it is a decimal point in a
            // value like `0.28`, of which the generated timing maths emits a great many.
            if (style[i] != '.' || (i > 0 && !char.IsWhiteSpace(style[i - 1]) && style[i - 1] is not (',' or '{' or '}')))
            {
                continue;
            }

            var end = i + 1;
            while (end < style.Length && (char.IsLetterOrDigit(style[end]) || style[end] == '-'))
            {
                end++;
            }

            if (end > i + 1)
            {
                yield return style[(i + 1)..end];
            }
        }
    }

    private static IEnumerable<string> KeyframeNames(string html)
    {
        var style = StyleBlock(html);
        const string Marker = "@keyframes ";
        var at = style.IndexOf(Marker, StringComparison.Ordinal);

        while (at >= 0)
        {
            var start = at + Marker.Length;
            var end = start;
            while (end < style.Length && (char.IsLetterOrDigit(style[end]) || style[end] == '-'))
            {
                end++;
            }

            yield return style[start..end];
            at = style.IndexOf(Marker, end, StringComparison.Ordinal);
        }
    }

    private static string BakedSvgPath() => Path.Combine(RepositoryRoot(), "assets", "rask-flow.svg");

    private static string SourceRoot() => Path.Combine(RepositoryRoot(), "src");

    // Walk up to the directory holding the solution, so the test does not depend on where the runner
    // parked the assembly.
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }
}
