using Rask.Site;
using Rask.Site.Features;

namespace Rask.Site.Tests.Guides;

// A comment on its own in a doc is a note to the doc's editor: GitHub hides it, and the site used to ship it
// in the page body (elements.md's note on its MDN link table went out on every visit). The renderer drops
// it; a comment that is SHOWN — inside a fenced sample, or inline code — is content and stays.
public sealed class MarkdownCommentTests
{
    [Fact]
    public void A_comment_block_is_not_rendered()
    {
        var html = Render("Before.\n\n<!-- a note for the editor,\n     over two lines -->\n\nAfter.");

        Assert.DoesNotContain("<!--", html, StringComparison.Ordinal);
        Assert.DoesNotContain("a note for the editor", html, StringComparison.Ordinal);
        Assert.Contains("<p>Before.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>After.</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_in_a_fenced_sample_is_shown_as_code()
    {
        var html = Render("```xml\n<!-- Don't. -->\n<Flag>false</Flag>\n```");

        Assert.Contains("Don&#39;t.", html.Replace("&#x27;", "&#39;", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("&lt;!--", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_in_inline_code_is_shown_as_code()
    {
        var html = Render("A conditional comment (`<!--[if IE]>`) is kept.");

        Assert.Contains("<code>&lt;!--[if IE]&gt;</code>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Markup_after_a_comment_on_the_same_line_is_kept()
    {
        var html = Render("<!-- label --><div class=\"kept\">x</div>");

        Assert.Contains("class=\"kept\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_guide_ships_a_raw_comment()
    {
        var offenders = new List<string>();

        foreach (var guide in GuideCatalog.All)
        {
            foreach (var segment in Markdown.Split(GuideCatalog.ReadMarkdown(guide.Slug)!))
            {
                if (!segment.IsDemo && Markdown.RenderHtml((null, segment.Value)).Contains("<!--", StringComparison.Ordinal))
                {
                    offenders.Add(guide.Slug);
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string Render(string markdown) => Markdown.RenderHtml((null, markdown));
}
