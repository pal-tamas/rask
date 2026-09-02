using Rask.Example.Site.Pages;
using Rask.Testing;

namespace Rask.Example.Site.Tests;

/// <summary>
/// Every "read the guide" link on the landing page points at a guide that exists.
/// </summary>
/// <remarks>
/// <para>
/// The cards are the site's main way into the documentation — each one names a feature and opens the
/// guide about it. The slug is a bare string in the markup, and nothing about a wrong one is visible
/// from inside this repo: the docs are a different application, published to a different directory, so
/// a renamed or deleted guide leaves a card that builds, renders, styles correctly, and 404s. On the
/// framework's own front door.
/// </para>
/// <para>
/// Asserted against <c>docs/</c> on disk rather than against a list kept here, for the reason every
/// other coverage guard in this repo gives: a second hand-written list is the thing that goes stale.
/// The docs app resolves <c>/guides/{slug}</c> to the doc with that leaf name, so a file existing is
/// exactly the condition for the link resolving.
/// </para>
/// </remarks>
public sealed partial class GuideLinkTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Every_guide_link_on_the_page_resolves_to_a_doc_that_exists()
    {
        var docs = DocsDirectory();
        var slugs = GuideSlugs();

        // Guard the extractor itself. A regex that silently matched nothing would make the assertion
        // below pass on a page with no links at all, which is the failure this file exists to catch.
        Assert.True(
            slugs.Count >= 20,
            $"only {slugs.Count} guide link(s) were found in the rendered page — the extractor is "
            + "probably not matching the markup any more, which would make this test vacuous.");

        var missing = slugs
            .Where(slug => !File.Exists(Path.Combine(docs, slug + ".md")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"The landing page links to {string.Join(", ", missing)}, which do not exist under docs/. "
            + "Every card opens /guides/{slug} in the docs app, and that route resolves a slug to the "
            + "doc with the same leaf name — so these render as 404s on the framework's front door.");
    }

    [Fact]
    public void Every_guide_link_leaves_the_app_safely()
    {
        // target="_blank" without rel="noopener" hands the opened page a live window.opener back into
        // this one. The framework's own site is the last place to demonstrate that.
        var html = Render();

        var blank = Occurrences(html, "target=\"_blank\"");
        var noopener = Occurrences(html, "rel=\"noopener\"");

        Assert.True(
            noopener >= blank,
            $"the page has {blank} target=\"_blank\" link(s) but only {noopener} carry rel=\"noopener\".");
    }

    private static string Render() => RaskTest.Render(() => HomePage).Html;

    private static List<string> GuideSlugs()
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            Render(),
            "href=\"docs/guides/(?<slug>[a-z0-9-]+)\"");

        return matches.Select(m => m.Groups["slug"].Value).ToList();
    }

    /// <summary>The repo's <c>docs/</c>, found by walking up from the test binary.</summary>
    private static string DocsDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "docs");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("could not find the repository's docs/ directory.");
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var cursor = 0;
        while (true)
        {
            var hit = haystack.IndexOf(needle, cursor, StringComparison.Ordinal);
            if (hit < 0)
            {
                return count;
            }

            count++;
            cursor = hit + needle.Length;
        }
    }
}
