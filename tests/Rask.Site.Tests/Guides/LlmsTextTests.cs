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
    public void ATopLevelDocsLinksLeaveTheRepositoryForThePlacesTheyPointAt()
    {
        var twin = LlmsText.Twin(
            "See [routing](routing.md#route-parameters), [the readme](../README.md), [the index](README.md), "
            + "[the tests](../tests/Rask.Cqrs.Tests \"the suite\"), [the kit](/docs/ui/actions) and "
            + "[elsewhere](https://example.com/x.md).\n"
            + "\n[ref]: cqrs.md#behaviors\n",
            "sqlite.md");

        // A guide becomes its page on the site — fragment kept, slash on, so it is the canonical URL.
        Assert.Contains("](https://rask.sh/docs/guides/routing/#route-parameters)", twin, StringComparison.Ordinal);

        // Anything that is not a guide is a file in the repository, where it lives — Markdown or not, with
        // its title kept.
        Assert.Contains("](https://github.com/pal-tamas/rask/blob/main/README.md)", twin, StringComparison.Ordinal);
        Assert.Contains("](https://github.com/pal-tamas/rask/blob/main/docs/README.md)", twin, StringComparison.Ordinal);
        Assert.Contains(
            "](https://github.com/pal-tamas/rask/blob/main/tests/Rask.Cqrs.Tests \"the suite\")",
            twin,
            StringComparison.Ordinal);

        // A site-rooted path gains the site, since llms-full.txt is read out of context.
        Assert.Contains("](https://rask.sh/docs/ui/actions)", twin, StringComparison.Ordinal);

        // Absolute links are the author's and stay as written; reference definitions are links too.
        Assert.Contains("](https://example.com/x.md)", twin, StringComparison.Ordinal);
        Assert.Contains("[ref]: https://rask.sh/docs/guides/cqrs/#behaviors", twin, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedDocsLinksResolveAgainstItsOwnFolder()
    {
        var twin = LlmsText.Twin(
            "[the matrix](../browser-capabilities.md), [a sibling](notes.md) and [a benchmark](../../tests/Bench/sqlite.md)\n",
            "apis/geolocation.md");

        // Climbing a directory to reach a guide still reaches the guide.
        Assert.Contains("](https://rask.sh/docs/guides/browser-capabilities/)", twin, StringComparison.Ordinal);

        // A sibling is a sibling — docs/apis/notes.md, not docs/notes.md.
        Assert.Contains("](https://github.com/pal-tamas/rask/blob/main/docs/apis/notes.md)", twin, StringComparison.Ordinal);

        // Where a link lands decides, not its file name: a benchmark's sqlite.md is not the SQLite guide.
        Assert.Contains("](https://github.com/pal-tamas/rask/blob/main/tests/Bench/sqlite.md)", twin, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeIsLeftExactlyAsWritten()
    {
        // A fence or a code span showing Markdown or C# is showing TEXT. Rewriting a link inside it — or
        // mistaking `handlers[0](context)` for one — changes the example.
        const string Fenced = "```md\n[routing](routing.md)\n<!-- demo:counter -->\n```\n";
        Assert.Equal(Fenced, LlmsText.Twin(Fenced, "cqrs.md"));

        var inline = LlmsText.Twin("Call `handlers[0](context)`, then read [the CLI](cli.md).\n", "cqrs.md");
        Assert.Contains("`handlers[0](context)`", inline, StringComparison.Ordinal);
        Assert.Contains("[the CLI](https://rask.sh/docs/guides/cli/)", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void ADemoMarkerLineIsDropped()
    {
        var twin = LlmsText.Twin("Before.\n\n<!-- demo:binding-typed -->\n\nAfter.\n", "forms.md");

        Assert.DoesNotContain("demo:", twin, StringComparison.Ordinal);
        Assert.Contains("Before.", twin, StringComparison.Ordinal);
        Assert.Contains("After.", twin, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPublishedTwinKeepsALinkThatOnlyResolvesInTheRepository()
    {
        // Over the real docs rather than a sample, because the docs are where the link shapes nobody thought
        // of live. It checked ".md" targets only, and so could not see ../tests/Rask.Cqrs.Tests and a dozen
        // links like it — served from /docs/guides/, every one of them 404s. Any relative or site-rooted link
        // left in prose is a failure; code is exempt, for the reason the twin leaves it alone.
        var leftovers = new List<string>();
        foreach (var guide in GuideCatalog.All)
        {
            var twin = LlmsText.Twin(GuideCatalog.ReadMarkdown(guide.Slug)!, GuideCatalog.SourcePath(guide.Slug));
            foreach (var prose in Prose(twin))
            {
                foreach (Match match in UnresolvedLink().Matches(prose))
                {
                    leftovers.Add($"{guide.Slug}: {match.Value}");
                }
            }
        }

        Assert.True(leftovers.Count == 0, "links that do not resolve off the repository: " + string.Join("; ", leftovers.Take(10)));
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

    /// <summary>The prose of a Markdown document: outside fences, and outside inline code spans.</summary>
    private static IEnumerable<string> Prose(string markdown)
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

            if (fenced)
            {
                continue;
            }

            var parts = line.Split('`');
            for (var i = 0; i < parts.Length; i += 2)
            {
                yield return parts[i];
            }
        }
    }

    // ](target) where target has no scheme and is not a bare fragment — relative or rooted, either way a path
    // that means nothing to a reader holding only this file.
    [GeneratedRegex(@"\]\((?![a-zA-Z][a-zA-Z0-9+.-]*:|#)[^)\s]+(\s+""[^""]*"")?\)")]
    private static partial Regex UnresolvedLink();
}
