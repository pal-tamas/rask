using System.Text.RegularExpressions;
using Rask.Site.Features;

namespace Rask.Site.Tests.Guides;

/// <summary>
///     <c>/llms.txt</c>, <c>/llms-full.txt</c> and the per-guide Markdown twins say what the site says, and
///     every link in them resolves off the repository.
/// </summary>
public sealed partial class LlmsTextTests
{
    [Fact]
    public void TheIndexNamesEveryGuideByItsTwinAndItsDescription()
    {
        var index = LlmsText.Index();

        // The one element the convention requires, then the summary blockquote a tool shows first.
        Assert.StartsWith($"# {SiteIdentity.Name}\n\n> {SiteIdentity.Description}\n", index, StringComparison.Ordinal);

        foreach (var guide in GuideCatalog.All)
        {
            Assert.Contains(
                $"- [{guide.Title}]({LlmsText.MarkdownUrl(guide.Slug)}): {guide.Description}\n",
                index,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheContributorNotesAreTheOptionalSectionAndComeLast()
    {
        // "Optional" is the name the convention gives the section a tool short on context may drop, and it
        // only works as that if nothing required comes after it.
        var index = LlmsText.Index();
        var optional = index.IndexOf("\n## Optional\n", StringComparison.Ordinal);

        Assert.True(optional > 0, "the index has no Optional section");
        Assert.Equal(optional, index.LastIndexOf("\n## ", StringComparison.Ordinal));
        Assert.DoesNotContain("\n## Contributing & internals\n", index, StringComparison.Ordinal);
    }

    [Fact]
    public void ATwinsLinksLeaveTheRepositoryForThePlacesTheyPointAt()
    {
        var twin = LlmsText.Twin(
            "See [routing](routing.md#route-parameters), [the matrix](../browser-capabilities.md), "
            + "[the readme](../README.md), [the index](README.md) and [elsewhere](https://example.com/x.md).\n"
            + "\n[ref]: cqrs.md#behaviors\n");

        // A guide becomes its page on the site — fragment kept, slash on, so it is the canonical URL.
        Assert.Contains("](https://rask.sh/docs/guides/routing/#route-parameters)", twin, StringComparison.Ordinal);

        // Climbing a directory to reach a guide still reaches the guide.
        Assert.Contains("](https://rask.sh/docs/guides/browser-capabilities/)", twin, StringComparison.Ordinal);

        // Anything that is not a guide is a file in the repository, where it lives.
        Assert.Contains("](https://github.com/pal-tamas/rask/blob/main/README.md)", twin, StringComparison.Ordinal);
        Assert.Contains("](https://github.com/pal-tamas/rask/blob/main/docs/README.md)", twin, StringComparison.Ordinal);

        // Absolute links are the author's and stay as written; reference definitions are links too.
        Assert.Contains("](https://example.com/x.md)", twin, StringComparison.Ordinal);
        Assert.Contains("[ref]: https://rask.sh/docs/guides/cqrs/#behaviors", twin, StringComparison.Ordinal);
    }

    [Fact]
    public void AFenceIsLeftExactlyAsWritten()
    {
        // A fence showing Markdown is showing TEXT. Rewriting the link inside it changes the example.
        const string Markdown = "```md\n[routing](routing.md)\n<!-- demo:counter -->\n```\n";

        Assert.Equal(Markdown, LlmsText.Twin(Markdown));
    }

    [Fact]
    public void ADemoMarkerLineIsDropped()
    {
        var twin = LlmsText.Twin("Before.\n\n<!-- demo:binding-typed -->\n\nAfter.\n");

        Assert.DoesNotContain("demo:", twin, StringComparison.Ordinal);
        Assert.Contains("Before.", twin, StringComparison.Ordinal);
        Assert.Contains("After.", twin, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPublishedTwinKeepsARelativeLink()
    {
        // Over the real docs rather than a sample, because the docs are where the link shapes nobody thought
        // of live. A relative .md link left in a served file resolves against /docs/guides/ and 404s.
        var leftovers = new List<string>();
        foreach (var guide in GuideCatalog.All)
        {
            var twin = LlmsText.Twin(GuideCatalog.ReadMarkdown(guide.Slug)!);
            foreach (var line in OutsideFences(twin))
            {
                if (RelativeMarkdownLink().Match(line) is { Success: true } match)
                {
                    leftovers.Add($"{guide.Slug}: {match.Value}");
                }
            }
        }

        Assert.True(leftovers.Count == 0, "relative links survived: " + string.Join("; ", leftovers.Take(10)));
    }

    [Fact]
    public void TheFullFileCarriesEveryGuideButTheOptionalOnesWithTheirSource()
    {
        var full = LlmsText.Full();

        foreach (var guide in GuideCatalog.All)
        {
            var source = $"\nSource: {LlmsText.PageUrl(guide.Slug)}\n";
            if (guide.Group == "Contributing & internals")
            {
                Assert.DoesNotContain(source, full, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(source, full, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void WriteAllPutsEachTwinAtItsPagesAddressPlusMd()
    {
        var root = Path.Combine(Path.GetTempPath(), "rask-llms-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var written = LlmsText.WriteAll(root);

            Assert.Equal(GuideCatalog.All.Length, written);
            Assert.Equal(LlmsText.Index(), File.ReadAllText(Path.Combine(root, "llms.txt")));
            Assert.True(File.Exists(Path.Combine(root, "llms-full.txt")));

            // /docs/guides/cqrs/ is the page; /docs/guides/cqrs.md is the twin beside its directory.
            Assert.Equal("/docs/guides/cqrs.md", LlmsText.MarkdownPath("cqrs"));
            Assert.True(File.Exists(Path.Combine(root, "docs", "guides", "cqrs.md")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private static IEnumerable<string> OutsideFences(string markdown)
    {
        var fenced = false;
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced)
            {
                yield return line;
            }
        }
    }

    [GeneratedRegex(@"\]\((?![a-zA-Z][a-zA-Z0-9+.-]*:|#)[^)\s]*\.md(#[^)\s]*)?\)")]
    private static partial Regex RelativeMarkdownLink();
}
