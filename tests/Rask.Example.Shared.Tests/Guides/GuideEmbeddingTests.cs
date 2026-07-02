using Rask.Example.Shared;
using Rask.Example.Shared.Features;

namespace Rask.Example.Shared.Tests.Guides;

// The guides-first embedding contract: every `<!-- demo:key -->` marker a guide ships resolves to a
// registered demo (no dead embeds), and the segmenter splits prose from demos correctly. These are the
// guards that let guide authors add markers without wiring anything up by hand.
public sealed class GuideEmbeddingTests
{
    [Fact]
    public void EveryDemoMarkerInEveryGuide_ResolvesToARegisteredDemo()
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
    public void PilotGuides_ActuallyEmbedDemos()
    {
        // Phase 1 wires two pilot guides; if a refactor drops their markers the whole feature silently
        // reverts to plain prose, so assert the pilots still carry embeds.
        Assert.NotEmpty(Markdown.DemoKeys(GuideCatalog.ReadMarkdown("routing")!));
        Assert.NotEmpty(Markdown.DemoKeys(GuideCatalog.ReadMarkdown("forms")!));
    }

    [Fact]
    public void Split_InterleavesProseAndDemos_InDocumentOrder()
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
    public void Split_DropsBlankProseBetweenAdjacentMarkers()
    {
        const string md = "<!-- demo:binding-typed -->\n\n<!-- demo:binding-multi -->";

        var segments = Markdown.Split(md);

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.True(s.IsDemo));
    }

    [Fact]
    public void Split_NoMarkers_IsASingleProseSegment()
    {
        var segments = Markdown.Split("# Title\n\nJust prose, no demos.");

        var only = Assert.Single(segments);
        Assert.False(only.IsDemo);
    }

    [Fact]
    public void MarkerIsWhitespaceTolerant()
    {
        Assert.Equal(["binding-typed"], Markdown.DemoKeys("<!--   demo:  binding-typed   -->"));
    }

    [Fact]
    public void HighlightCodeBlocks_TokenizesAKnownLanguageFence()
    {
        // Markdig renders a fenced ```csharp block as this (body HTML-encoded, no highlighting).
        const string markdig = "<pre><code class=\"language-csharp\">var x = 1;</code></pre>";

        var html = Markdown.HighlightCodeBlocks(markdig);

        // Server-side tokenization injects ColorCode token <span class="...">s and keeps the language class.
        Assert.Contains("<span class=\"", html);
        Assert.Contains("language-csharp", html);
    }

    [Fact]
    public void HighlightCodeBlocks_TokenizesABashFence()
    {
        // The getting-started guide's `dotnet new …` blocks are ```bash; ColorCode has no shell lexer,
        // so BashLanguage supplies comment/string/keyword rules.
        const string markdig = "<pre><code class=\"language-bash\">dotnet new rask -o MyApp # scaffold</code></pre>";

        var html = Markdown.HighlightCodeBlocks(markdig);

        Assert.Contains("language-bash", html);
        Assert.Contains("class=\"comment\"", html);   // the trailing "# scaffold" comment is tokenized
    }

    [Fact]
    public void HighlightCodeBlocks_LeavesAnUnknownLanguageFenceUntouched()
    {
        const string plain = "<pre><code class=\"language-text\">just text</code></pre>";

        Assert.Equal(plain, Markdown.HighlightCodeBlocks(plain));
    }

    [Fact]
    public void HighlightCodeBlocks_DecodesEntitiesBeforeTokenizing()
    {
        // Markdig HTML-encodes the source; the highlighter must decode before tokenizing so the rendered
        // token text is the real code (a single re-encoded '<', not the literal "&lt;").
        const string markdig = "<pre><code class=\"language-csharp\">if (a &lt; b) { }</code></pre>";

        var html = Markdown.HighlightCodeBlocks(markdig);

        Assert.DoesNotContain("&amp;lt;", html);
    }
}
