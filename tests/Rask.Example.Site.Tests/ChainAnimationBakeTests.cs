using Rask.Example.Site;
using Rask.Testing;

namespace Rask.Example.Site.Tests;

// The README needs the hero animation as a real file — GitHub cannot run a component. That file is not a
// second source: it is BAKED from ChainAnimation and compared here byte-for-byte, so the two can never
// drift. Re-bake after editing the component with:
//
//     RASK_BAKE_CHAIN_SVG=1 dotnet test tests/Rask.Example.Site.Tests
//
// This is also the landing app's first unit coverage of any kind; everything else about it is E2E-only.
public partial class ChainAnimationBakeTests : global::Rask.Core.RaskMarkup
{
    private const string BakeEnvironmentVariable = "RASK_BAKE_CHAIN_SVG";

    [Fact]
    public void Baked_svg_matches_the_component()
    {
        var rendered = RaskTest.Render(() => ChainAnimation).Html;
        var path = BakedSvgPath();

        if (Environment.GetEnvironmentVariable(BakeEnvironmentVariable) is not (null or ""))
        {
            File.WriteAllText(path, rendered + "\n");
        }

        Assert.True(
            File.Exists(path),
            $"{path} is missing. Bake it with {BakeEnvironmentVariable}=1 dotnet test tests/Rask.Example.Site.Tests.");

        // Normalised on the trailing newline only: the file keeps one so it is a well-behaved text file in
        // git, while the renderer emits none.
        Assert.Equal(rendered, File.ReadAllText(path).TrimEnd('\n', '\r'));
    }

    // The three properties the README asset depends on, asserted on the markup rather than on the file, so
    // a component edit that breaks one fails here instead of silently baking a broken asset.
    [Fact]
    public void Baked_svg_is_a_standalone_document()
    {
        var html = RaskTest.Render(() => ChainAnimation).Html;

        // Without the namespace the file is not a valid SVG document and will not render through <img>.
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", html, StringComparison.Ordinal);

        // Every colour must carry a literal fallback: standalone, none of the site's custom properties are
        // defined, so a bare var(--accent) would paint nothing.
        Assert.DoesNotContain("var(--accent)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("var(--panel)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("var(--ink)", html, StringComparison.Ordinal);

        // Motion has to be escapable, and the base state is authored as the final frame so that turning the
        // animations off lands on the finished picture rather than a blank one.
        Assert.Contains("prefers-reduced-motion: reduce", html, StringComparison.Ordinal);
    }

    // An SVG <style> inline in an HTML document is not scoped to the SVG — its rules apply to the whole
    // page. Every selector and keyframe therefore has to be prefixed, or the hero would restyle the site
    // around it.
    [Fact]
    public void Every_class_and_keyframe_is_prefixed()
    {
        var html = RaskTest.Render(() => ChainAnimation).Html;

        foreach (var selector in FindAll(html, '.'))
        {
            Assert.StartsWith("rc-", selector, StringComparison.Ordinal);
        }

        foreach (var name in KeyframeNames(html))
        {
            Assert.StartsWith("rc-", name, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> FindAll(string html, char sigil)
    {
        var style = StyleBlock(html);
        for (var i = 0; i < style.Length; i++)
        {
            if (style[i] != sigil || (i > 0 && !char.IsWhiteSpace(style[i - 1]) && style[i - 1] is not (',' or '{' or '}')))
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
        const string marker = "@keyframes ";
        for (var i = style.IndexOf(marker, StringComparison.Ordinal); i >= 0;
             i = style.IndexOf(marker, i + 1, StringComparison.Ordinal))
        {
            var start = i + marker.Length;
            var end = start;
            while (end < style.Length && (char.IsLetterOrDigit(style[end]) || style[end] == '-'))
            {
                end++;
            }

            yield return style[start..end];
        }
    }

    private static string StyleBlock(string html)
    {
        var open = html.IndexOf("<style", StringComparison.Ordinal);
        open = html.IndexOf('>', open) + 1;
        var close = html.IndexOf("</style>", open, StringComparison.Ordinal);
        return html[open..close];
    }

    private static string BakedSvgPath()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return Path.Combine(dir, "assets", "rask-chain.svg");
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}
