using System.Text.RegularExpressions;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;

namespace Rask.Example.Shared.Tests.Pages;

/// <summary>
/// <see cref="DocsIndexTests"/> guards that every doc is <em>reachable</em>; this guards that the links
/// inside one <em>resolve</em>. They are different failures: an unreachable doc can't be found, while a
/// broken link is found and then goes nowhere — and the docs are the product's front door.
///
/// <para>Three checks, each for a miss that shipped. A link to a file that doesn't exist (easy to write
/// across two branches, where the target lands in the other one). A link to a doc the app can't serve — the
/// renderer rewrites <c>x.md</c> to the SPA route <c>/guides/x</c>, so a doc that isn't embedded under that
/// slug renders a 404 rather than a dead file. And an anchor that names no heading, which is the worst of
/// the three because it still navigates: the reader lands at the top of the page and never knows they were
/// sent to the wrong place.</para>
///
/// <para>Anchors are checked against the ids <em>Markdig's AutoIdentifiers actually stamps</em>, by parsing
/// with the renderer's own <see cref="Markdown.Pipeline"/> — a hand-rolled slugifier here would be a second
/// implementation to disagree with the first.</para>
/// </summary>
public sealed partial class DocsLinkTests
{
    [Fact]
    public void Every_relative_doc_link_points_at_a_doc_that_exists()
    {
        var broken = AllLinks()
            .Where(link => link.Path is not null && !File.Exists(Path.Combine(DocsDirectory(), link.Path)))
            .Select(link => $"{link.Source} → {link.Target}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            broken.Length == 0,
            "These links point at a doc that doesn't exist. A link written against a file that lands in "
            + $"another branch ships a 404:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", broken));
    }

    [Fact]
    public void Every_linked_doc_is_servable_under_the_slug_the_renderer_rewrites_to()
    {
        // Markdown.RewriteLinks turns `dir/x.md#frag` into `/guides/x#frag`, keying off the BARE LEAF. So a
        // doc that exists on disk but isn't embedded under that slug is a link the reader can follow to a
        // 404 — the file being present is not enough.
        var unservable = AllLinks()
            .Where(link => link.Path is not null && File.Exists(Path.Combine(DocsDirectory(), link.Path)))
            .Where(link => GuideCatalog.ReadMarkdown(Slug(link.Path!)) is null)
            .Select(link => $"{link.Source} → {link.Target} (would route to /guides/{Slug(link.Path!)})")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unservable.Length == 0,
            "These links resolve on disk but the app can't serve the route they rewrite to, so following "
            + $"one 404s:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", unservable));
    }

    [Fact]
    public void Every_anchor_names_a_heading_in_the_document_it_points_at()
    {
        var docs = DocsDirectory();
        var anchors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var dangling = new List<string>();
        foreach (var link in AllLinks().Where(l => l.Fragment.Length > 0))
        {
            // A link whose file is missing is already reported by the test above; don't report it twice.
            var target = link.Path ?? link.SourcePath;
            var full = Path.Combine(docs, target);
            if (!File.Exists(full))
            {
                continue;
            }

            if (!anchors.TryGetValue(target, out var ids))
            {
                anchors[target] = ids = HeadingIds(File.ReadAllText(full));
            }

            if (!ids.Contains(link.Fragment))
            {
                dangling.Add($"{link.Source} → {link.Target}");
            }
        }

        Assert.True(
            dangling.Count == 0,
            "These anchors name no heading in the document they point at. The link still navigates, so the "
            + $"reader lands at the top of the page instead — usually a heading that was reworded or is "
            + $"bold text rather than a heading:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", dangling.Order(StringComparer.Ordinal)));
    }

    /// <summary>The heading ids Markdig stamps for <paramref name="source"/>, at every level.</summary>
    /// <remarks>
    /// Not <c>Markdown.Headings</c>: that surfaces the TOC and so keeps only <c>##</c>/<c>###</c>, while an
    /// anchor may legitimately point at any heading. Same pipeline, so the ids are the rendered ones.
    /// </remarks>
    private static HashSet<string> HeadingIds(string source) =>
        Markdig.Markdown.Parse(source, Markdown.Pipeline)
            .Descendants<HeadingBlock>()
            .Select(h => h.GetAttributes().Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal)!;

    /// <summary>The route slug the renderer rewrites a doc path to — its bare leaf, without ".md".</summary>
    private static string Slug(string docPath) =>
        Path.GetFileNameWithoutExtension(docPath);

    private readonly record struct DocLink(string Source, string SourcePath, string Target, string? Path, string Fragment);

    /// <summary>
    /// Every in-repo markdown link in every doc, with its target split into a docs-relative path and an
    /// anchor. Skipped: external schemes (checking those needs the network, which buys flakiness), and
    /// <c>../</c> paths — the renderer sends those to GitHub rather than routing them, so they aren't the
    /// app's problem and they point outside <c>docs/</c>.
    /// </summary>
    private static IEnumerable<DocLink> AllLinks()
    {
        var docs = DocsDirectory();
        foreach (var file in Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var sourcePath = Path.GetRelativePath(docs, file).Replace('\\', '/');
            var folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? string.Empty;

            foreach (Match match in LinkRegex().Matches(File.ReadAllText(file)))
            {
                var target = match.Groups[1].Value.Trim();
                if (target.Length == 0
                    || target.Contains("://", StringComparison.Ordinal)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    || target.Contains("../", StringComparison.Ordinal))
                {
                    continue;
                }

                var hash = target.IndexOf('#', StringComparison.Ordinal);
                var path = hash < 0 ? target : target[..hash];
                var fragment = hash < 0 ? string.Empty : target[(hash + 1)..];

                // Only .md targets route through the guide pages; an image or a code file linked relatively
                // is a plain asset and out of scope here.
                if (path.Length > 0 && !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resolved = path.Length == 0
                    ? null
                    : Normalize(folder.Length == 0 ? path : $"{folder}/{path}");

                yield return new DocLink(sourcePath, sourcePath, target, resolved, fragment);
            }
        }
    }

    /// <summary>Collapses "tutorial/../cli.md" and the like without touching the filesystem.</summary>
    private static string Normalize(string combined) =>
        new Uri(new Uri("doc:///"), combined).AbsolutePath.TrimStart('/');

    // Markdown inline links: ](target). Deliberately not matching reference-style or bare autolinks —
    // the docs use inline links throughout, and a narrow regex beats a wrong one.
    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();

    private static string DocsDirectory()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return Path.Combine(dir, "docs");
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}
