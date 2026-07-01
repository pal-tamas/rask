using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Generated;

#pragma warning disable RASK014 // tests construct page components directly to drive ToHtml()

namespace Rask.Example.Shared.Tests.Pages;

// The Guides section: the Markdown component (renders docs/*.md with Markdig + SPA link rewriting),
// the GuideCatalog (embedded-doc lookup), and the index/detail pages.
public sealed class GuidesTests
{
    [Fact]
    public void Markdown_RendersHeadingsAndInlineMarkup()
    {
        var html = Markdown("# Title\n\nHello **world**.").ToHtml();
        Assert.Contains("<div class=\"markdown-body\">", html);
        Assert.Contains("Title", html);
        Assert.Contains("<strong>world</strong>", html);
    }

    [Fact]
    public void Markdown_RewritesInternalGuideLink_ToSpaRoute() =>
        Assert.Contains("href=\"/guides/routing\" data-rask-nav", Markdown("[Routing](routing.md)").ToHtml());

    [Fact]
    public void Markdown_RewritesFragmentAndSubdirLinks()
    {
        Assert.Contains("href=\"/guides/forms#binding\" data-rask-nav", Markdown("[x](forms.md#binding)").ToHtml());
        Assert.Contains("href=\"/guides/live-rendering\" data-rask-nav",
            Markdown("[x](architecture/live-rendering.md)").ToHtml());
    }

    [Fact]
    public void Markdown_RewritesRepoRootLink_ToGitHub() =>
        Assert.Contains("href=\"https://github.com/pal-tamas/rask/blob/main/README.md\"",
            Markdown("[readme](../README.md)").ToHtml());

    [Fact]
    public void Markdown_LeavesExternalAndAnchorLinksUntouched()
    {
        var html = Markdown("[g](https://example.com) and [a](#section)").ToHtml();
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
    public void GuidePage_KnownSlug_RendersGuideChromeWithMarkdownBody()
    {
        // GuidePage delegates to GuideChrome (a DI-ctor component), so it renders through a live context.
        var html = new GuidePage { Slug = "routing" }.RenderAsLiveRoot(TestServices.Default());
        Assert.Contains("markdown-body", html);
        Assert.Contains("All guides", html); // the back link
        Assert.Contains("guide-chapters", html); // the Rails-style Chapters TOC
    }

    [Fact]
    public void GuidePage_UnknownSlug_RendersNotFound()
    {
        var html = new GuidePage { Slug = "nope" }.RenderAsLiveRoot(TestServices.Default());
        Assert.Contains("No guide found", html);
        Assert.DoesNotContain("markdown-body", html);
    }

    [Fact]
    public void GuidesIndexPage_RendersEveryGroupAndGuide()
    {
        var html = new GuidesIndexPage().ToHtml();
        foreach (var group in GuideCatalog.GroupOrder)
        {
            Assert.Contains($">{group}<", html);
        }

        Assert.Contains("Getting started", html);
        Assert.Contains("href=\"/guides/routing\"", html);
    }
}
