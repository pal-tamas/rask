using System.Text.Encodings.Web;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

#pragma warning disable RASK014 // tests construct page components directly to drive ToHtml()

namespace Rask.Example.Shared.Tests.Pages;

// The Guides section: the Markdown component (renders docs/*.md with Markdig + SPA link rewriting),
// the GuideCatalog (embedded-doc lookup), and the index/detail pages.
public sealed partial class GuidesTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Markdown_RendersHeadingsAndInlineMarkup()
    {
        var html = Markdown.Source("# Title\n\nHello **world**.").ToHtml();
        Assert.Contains("<div class=\"markdown-body\">", html);
        Assert.Contains("Title", html);
        Assert.Contains("<strong>world</strong>", html);
    }

    [Fact]
    public void Markdown_RendersReferenceStyleMdnLinks_AsRealAnchors()
    {
        // The element catalog in docs/elements.md links all ~104 tags to MDN reference-style, with the
        // definitions collected at the end — inline URLs that long would bury the prose. A renderer
        // that did not resolve them would print the literal "[`a`][a]" on the docs site, so this pins
        // that the site's pipeline does.
        var html = Markdown.Source(
                "[`video`][video], [`wbr`][wbr]:\n\n"
                + "<!-- a comment between the prose and the definitions -->\n\n"
                + "[video]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/video\n"
                + "[wbr]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/wbr\n")
            .ToHtml();

        Assert.Contains(
            "href=\"https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/video\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[`video`][video]", html, StringComparison.Ordinal);
        // The definition lines must be consumed, not printed as text below the catalog.
        Assert.DoesNotContain("[wbr]:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_RewritesInternalGuideLink_ToSpaRoute() =>
        Assert.Contains("href=\"/guides/routing\" data-rask-nav", Markdown.Source("[Routing](routing.md)").ToHtml());

    [Fact]
    public void Markdown_RewritesFragmentAndSubdirLinks()
    {
        Assert.Contains("href=\"/guides/forms#binding\" data-rask-nav", Markdown.Source("[x](forms.md#binding)").ToHtml());
        Assert.Contains("href=\"/guides/live-rendering\" data-rask-nav",
            Markdown.Source("[x](architecture/live-rendering.md)").ToHtml());
    }

    [Fact]
    public void Markdown_RewritesRepoRootLink_ToGitHub() =>
        Assert.Contains("href=\"https://github.com/pal-tamas/rask/blob/main/README.md\"",
            Markdown.Source("[readme](../README.md)").ToHtml());

    [Fact]
    public void Markdown_LeavesExternalAndAnchorLinksUntouched()
    {
        var html = Markdown.Source("[g](https://example.com) and [a](#section)").ToHtml();
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.DoesNotContain("/guides/", html);
    }

    [Fact]
    public void GuideCatalog_ReadMarkdown_KnownSlug_ReturnsContent()
    {
        var md = GuideCatalog.ReadMarkdown("routing");
        Assert.NotNull(md);
        Assert.Contains("# Routing", md);
    }

    [Fact]
    public void GuideCatalog_ReadMarkdown_UnknownSlug_ReturnsNull() =>
        Assert.Null(GuideCatalog.ReadMarkdown("does-not-exist"));

    [Fact]
    public void GuideCatalog_EveryCuratedSlug_HasAnEmbeddedDoc()
    {
        // Guards against a typo'd slug in the catalog: every listed guide must resolve to an embedded
        // docs/{slug}.md, or its sidebar/index entry would 404.
        foreach (var g in GuideCatalog.All)
        {
            Assert.NotNull(GuideCatalog.ReadMarkdown(g.Slug));
        }
    }

    [Fact]
    public void GuideCatalog_EveryEmbeddedDoc_IsCataloged()
    {
        // The reverse guard: every user-facing doc embedded from docs/**/*.md must appear in the catalog,
        // so a doc can't be added to the repo yet silently hidden from the site. docs/README.md is the
        // docs index (the guides index page itself is its on-site equivalent) — the one exception.
        var slugs = GuideCatalog.All.Select(g => g.Slug).ToHashSet(StringComparer.Ordinal);
        var embedded = typeof(GuideCatalog).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("raskdoc/", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => n["raskdoc/".Length..^".md".Length])
            .Where(leaf => leaf != "README");

        foreach (var leaf in embedded)
        {
            Assert.True(slugs.Contains(leaf), $"docs/{leaf}.md is embedded but missing from GuideCatalog.All.");
        }
    }

    [Fact]
    public void GuidePage_KnownSlug_RendersGuideChromeWithMarkdownBody()
    {
        // GuidePage delegates to GuideChrome (a DI-ctor component), so it renders through a live context.
        var html = RaskTest.Render(new GuidePage { Slug = "routing" }, TestServices.Default()).Html;
        Assert.Contains("markdown-body", html);
        Assert.Contains("All guides", html); // the back link
        Assert.Contains("guide-chapters", html); // the Chapters TOC
    }

    [Fact]
    public void GuidePage_UnknownSlug_RendersNotFound()
    {
        var html = RaskTest.Render(new GuidePage { Slug = "nope" }, TestServices.Default()).Html;
        Assert.Contains("No guide found", html);
        Assert.DoesNotContain("markdown-body", html);
    }

    [Fact]
    public void GuidesIndexPage_RendersEveryGroupAndGuide()
    {
        var html = new GuidesIndexPage().ToHtml();
        foreach (var group in GuideCatalog.GroupOrder)
        {
            // Group headings are Text-encoded (e.g. "Mobile & devices" → "Mobile &amp; devices").
            Assert.Contains($">{HtmlEncoder.Default.Encode(group)}<", html);
        }

        Assert.Contains("Getting started", html);
        Assert.Contains("href=\"/guides/routing\"", html);
        // The Native guide (docs/native.md) is surfaced under the Mobile & devices group.
    }
}
