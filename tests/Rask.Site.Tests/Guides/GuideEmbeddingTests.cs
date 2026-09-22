using Rask.Site;
using Rask.Site.Features;

namespace Rask.Site.Tests.Guides;

// The guides-first embedding contract: every `<!-- demo:key -->` marker a guide ships resolves to a
// registered demo (no dead embeds), and the segmenter splits prose from demos correctly. These are the
// guards that let guide authors add markers without wiring anything up by hand.
public sealed class GuideEmbeddingTests
{
    [Fact]
    public void Every_demo_marker_in_every_guide_resolves_to_a_registered_demo()
    {
        var offenders = new List<string>();

        foreach (var guide in GuideCatalog.All)
        {
            var source = GuideCatalog.ReadMarkdown(guide.Slug);
            Assert.NotNull(source);

            foreach (var key in Markdown.DemoKeys(source!))
            {
                if (!DemoRegistry.Contains(key))
                {
                    offenders.Add($"{guide.Slug}.md → “{key}”");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Guides reference demo keys that aren't registered in DemoRegistry:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_pilot_guides_actually_embed_demos()
    {
        // Phase 1 wires two pilot guides; if a refactor drops their markers the whole feature silently
        // reverts to plain prose, so assert the pilots still carry embeds.
        Assert.NotEmpty(Markdown.DemoKeys(GuideCatalog.ReadMarkdown("routing")!));
        Assert.NotEmpty(Markdown.DemoKeys(GuideCatalog.ReadMarkdown("forms")!));
    }

    [Fact]
    public void Splitting_interleaves_prose_and_demos_in_document_order()
    {
        const string md = "intro prose\n\n<!-- demo:binding-typed -->\n\nmiddle prose\n\n<!-- demo:binding-multi -->\n";

        var segments = Markdown.Split(md);

        Assert.Collection(segments,
            s => Assert.False(s.IsDemo),
            s =>
            {
                Assert.True(s.IsDemo);
                Assert.Equal("binding-typed", s.Value);
            },
            s => Assert.False(s.IsDemo),
            s =>
            {
                Assert.True(s.IsDemo);
                Assert.Equal("binding-multi", s.Value);
            });
    }

    [Fact]
    public void Splitting_drops_blank_prose_between_adjacent_markers()
    {
        const string md = "<!-- demo:binding-typed -->\n\n<!-- demo:binding-multi -->";

        var segments = Markdown.Split(md);

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.True(s.IsDemo));
    }

    [Fact]
    public void A_document_with_no_markers_splits_into_a_single_prose_segment()
    {
        var segments = Markdown.Split("# Title\n\nJust prose, no demos.");

        var only = Assert.Single(segments);
        Assert.False(only.IsDemo);
    }

    [Fact]
    public void A_demo_marker_is_whitespace_tolerant()
    {
        Assert.Equal(["binding-typed"], Markdown.DemoKeys("<!--   demo:  binding-typed   -->"));
    }

    [Fact]
    public void Highlighting_tokenizes_a_known_language_fence()
    {
        // Markdig renders a fenced ```csharp block as this (body HTML-encoded, no highlighting).
        const string markdig = "<pre><code class=\"language-csharp\">var x = 1;</code></pre>";

        var html = Markdown.HighlightCodeBlocks(markdig);

        // Server-side tokenization injects ColorCode token <span class="...">s and keeps the language class.
        Assert.Contains("<span class=\"", html);
        Assert.Contains("language-csharp", html);
    }

    [Fact]
    public void Highlighting_tokenizes_a_bash_fence()
    {
        // The getting-started guide's `rask new …` blocks are ```bash; ColorCode has no shell lexer,
        // so BashLanguage supplies comment/string/keyword rules.
        const string markdig = "<pre><code class=\"language-bash\">rask new MyApp # scaffold</code></pre>";

        var html = Markdown.HighlightCodeBlocks(markdig);

        Assert.Contains("language-bash", html);
        Assert.Contains("class=\"comment\"", html);   // the trailing "# scaffold" comment is tokenized
    }

    [Fact]
    public void Highlighting_leaves_an_unknown_language_fence_untouched()
    {
        const string plain = "<pre><code class=\"language-text\">just text</code></pre>";

        Assert.Equal(plain, Markdown.HighlightCodeBlocks(plain));
    }

    [Fact]
    public void Highlighting_decodes_entities_before_tokenizing()
    {
        // Markdig HTML-encodes the source; the highlighter must decode before tokenizing so the rendered
        // token text is the real code (a single re-encoded '<', not the literal "&lt;").
        const string markdig = "<pre><code class=\"language-csharp\">if (a &lt; b) { }</code></pre>";

        var html = Markdown.HighlightCodeBlocks(markdig);

        Assert.DoesNotContain("&amp;lt;", html);
    }
}
