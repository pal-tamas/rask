using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Rask.Core;

namespace Rask.Example.Shared;

// A reusable Markdown renderer: pass it markdown Source and it renders to HTML with Markdig and injects
// the result via Raw() inside a .markdown-body wrapper (styled globally in wwwroot/global.css — the Raw
// HTML carries no scope id, so those prose rules can't be component-scoped). The rendered HTML is cached
// per source, so Markdig parses once. This is the showcase's prose component; it also rewrites the docs'
// relative cross-links so they work in the SPA: a link to another guide ("foo.md", optionally with a
// "#frag" or "dir/" prefix) becomes a SPA-routed /guides/{leaf} anchor (data-rask-nav), and a link up to
// the repo root (../README.md) points at GitHub.
//
// Guides can also embed a live demo inline with an HTML-comment marker — `<!-- demo:key -->`. When any
// marker is present the source is split at the markers: each prose run renders as its own Raw() HTML
// chunk and the demo (resolved from DemoRegistry) is mounted as a real child component between them, so
// the demo participates in the live diff like any other node. The marker is invisible when the same
// docs/*.md renders on GitHub, so the guides stay dual-purpose (repo docs + on-site).
public sealed partial class Markdown : Component
{
    // The advanced extensions cover tables, fenced code, task lists, etc. — the Markdown the guides
    // actually use — and bring AutoIdentifiers, which gives every heading an id. Thread-safe, reused.
    // Internal so GuideChrome extracts headings through the *same* pipeline, guaranteeing the ids it
    // links to match the ids stamped on the rendered <h2>/<h3>.
    //
    // The ids are then RESTAMPED GitHub-style. The docs under docs/ are authored and reviewed on GitHub,
    // and their in-page links are written against GitHub's anchors — which differ from Markdig's wherever
    // a heading holds punctuation, because GitHub deletes the character and keeps the space around it
    // ("Context & DI" → context--di) while Markdig collapses the run (context-di). Rendering Markdig's ids
    // meant 62 links across the docs still navigated and silently landed the reader at the top of the page.
    // Markdig's own AutoIdentifierOptions.GitHub does NOT close this gap (verified: identical output), so
    // the slug is ours. Subscribed AFTER UseAdvancedExtensions so this runs after AutoIdentifiers and wins.
    internal static readonly MarkdownPipeline Pipeline = BuildPipeline();

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions();
        builder.DocumentProcessed += StampGitHubHeadingIds;
        return builder.Build();
    }

    private static void StampGitHubHeadingIds(MarkdownDocument document)
    {
        // Per document, because GitHub disambiguates repeats within one page.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var slug = GitHubSlug(InlineText(heading.Inline));
            if (slug.Length == 0)
            {
                continue;
            }

            if (seen.TryGetValue(slug, out var count))
            {
                seen[slug] = count + 1;
                slug = $"{slug}-{count}";
            }
            else
            {
                seen[slug] = 1;
            }

            heading.GetAttributes().Id = slug;
        }
    }

    /// <summary>
    /// GitHub's heading slug: lower-case, drop everything that isn't a letter, digit, hyphen or
    /// underscore, and turn each remaining whitespace character into a hyphen.
    /// </summary>
    /// <remarks>
    /// The load-bearing detail is what it does NOT do: it never collapses the hyphens that result. A
    /// dropped "&amp;" or em dash leaves the spaces on either side of it, so they become two hyphens —
    /// which is why the docs are full of anchors like <c>#rask-db--ef-core-migrations</c>. Leading digits
    /// survive too (<c>#1-two-way-binding</c>), where Markdig strips them.
    /// </remarks>
    private static string GitHubSlug(string headingText)
    {
        var sb = new StringBuilder(headingText.Length);
        foreach (var ch in headingText)
        {
            if (char.IsWhiteSpace(ch))
            {
                sb.Append('-');
            }
            else if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static readonly ConcurrentDictionary<string, string> HtmlCache = new(StringComparer.Ordinal);

    // Non-nullable + no initializer ⇒ the factory generator emits Source as a required positional
    // parameter (mirrors CodeSample.Files). Rask assigns it after construction, so CS8618 is expected.
#pragma warning disable CS8618
    public string Source { get; set; }
#pragma warning restore CS8618

    protected override Component? Render() =>
        DemoMarkerRegex().IsMatch(Source)
            ? Div(Class: "markdown-body")[Segments()]
            : Div(Class: "markdown-body")[Raw(HtmlCache.GetOrAdd(Source, RenderHtml))];

    // Renders the split segments: prose runs become Raw() HTML chunks (each rendered and cached
    // independently) and each demo segment becomes the resolved demo component. An unknown key renders a
    // visible warning rather than silently vanishing (the registry-integrity test keeps guides from
    // shipping one).
    private IEnumerable<Component> Segments()
    {
        var index = 0;
        foreach (var segment in Split(Source))
        {
            if (!segment.IsDemo)
            {
                yield return Raw(HtmlCache.GetOrAdd(segment.Value, RenderHtml));
                continue;
            }

            yield return Div(Class: "guide-demo", Key: $"demo-{index}")[
                DemoRegistry.Contains(segment.Value)
                    ? DemoRegistry.Build(segment.Value)
                    : Div(Class: "alert alert-warning")[$"Unknown demo “{segment.Value}”."]
            ];
            index++;
        }
    }

    // A parsed piece of a guide: either a prose run (IsDemo == false, Value is the raw markdown chunk)
    // or a demo reference (IsDemo == true, Value is the demo key).
    internal readonly record struct Segment(bool IsDemo, string Value);

    // Splits markdown at every `<!-- demo:key -->` marker into alternating prose/demo segments, in order.
    // Markers always sit on their own blank-line-separated line, so each prose chunk is a self-contained
    // block that renders independently. Blank prose runs (e.g. between two adjacent markers) are dropped.
    // Pure and static so the segmentation is unit-testable without a render context.
    internal static IReadOnlyList<Segment> Split(string source)
    {
        var segments = new List<Segment>();
        var pos = 0;
        foreach (Match m in DemoMarkerRegex().Matches(source))
        {
            var prose = source[pos..m.Index];
            if (prose.Trim().Length > 0)
            {
                segments.Add(new Segment(false, prose));
            }

            segments.Add(new Segment(true, m.Groups["key"].Value));
            pos = m.Index + m.Length;
        }

        var tail = source[pos..];
        if (tail.Trim().Length > 0)
        {
            segments.Add(new Segment(false, tail));
        }

        return segments;
    }

    // The demo keys a guide references, in document order — used by the registry-integrity test.
    internal static IReadOnlyList<string> DemoKeys(string source) =>
        Split(source).Where(s => s.IsDemo).Select(s => s.Value).ToArray();

    private static string RenderHtml(string source) =>
        HighlightCodeBlocks(RewriteLinks(global::Markdig.Markdown.ToHtml(source, Pipeline)));

    // Markdig renders a fenced ```lang block as <pre><code class="language-{lang}">{HTML-encoded source}
    // </code></pre> with NO highlighting. Tokenize the known languages server-side with the shared
    // ColorCode highlighter so guide prose code reads the same as the CodeSample demo panes (coloured by
    // the .markdown-body pre token rules in global.css); unknown languages pass through untouched.
    internal static string HighlightCodeBlocks(string html) =>
        CodeBlockRegex().Replace(html, m =>
        {
            var language = SyntaxHighlighter.LanguageFor(m.Groups["lang"].Value);
            if (language is null)
            {
                return m.Value;
            }

            var source = System.Net.WebUtility.HtmlDecode(m.Groups["body"].Value);
            // Keep Markdig's original language class (e.g. language-csharp); the token colours come from
            // the descendant .markdown-body pre span rules, so the class is just a label.
            return $"<pre><code class=\"language-{m.Groups["lang"].Value}\">"
                + SyntaxHighlighter.Highlight(source, language) + "</code></pre>";
        });

    private static string RewriteLinks(string html) =>
        DocLinkRegex().Replace(html, m =>
        {
            var path = m.Groups["path"].Value;
            var fragment = m.Groups["frag"].Value;
            if (path.Contains("../", StringComparison.Ordinal))
            {
                var leaf = path[(path.LastIndexOf('/') + 1)..];
                return $"href=\"https://github.com/pal-tamas/rask/blob/main/{leaf}{fragment}\"";
            }

            var slug = path[(path.LastIndexOf('/') + 1)..^".md".Length];
            return $"href=\"/guides/{slug}{fragment}\" data-rask-nav";
        });

    // A guide heading surfaced in the Chapters TOC / on-this-page rail: its level (2 or 3), plain text,
    // and the anchor id Markdig's AutoIdentifiers stamps on the rendered element.
    internal readonly record struct Heading(int Level, string Text, string Id);

    // Extracts the ## / ### headings from a guide's markdown, in document order, through the shared
    // Pipeline so the ids line up with the rendered anchors. Demo markers are HTML comments, so Markdig
    // ignores them and they never pollute the chapter list.
    internal static IReadOnlyList<Heading> Headings(string source)
    {
        var doc = global::Markdig.Markdown.Parse(source, Pipeline);
        var headings = new List<Heading>();
        foreach (var block in doc.Descendants<HeadingBlock>())
        {
            if (block.Level is not (2 or 3))
            {
                continue;
            }

            var id = block.GetAttributes().Id;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            headings.Add(new Heading(block.Level, InlineText(block.Inline), id));
        }

        return headings;
    }

    // Flattens a heading's inline content to plain text (literal runs + inline `code` spans), dropping
    // emphasis/link markup — enough for a TOC label.
    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var node in inline.Descendants())
        {
            switch (node)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    // Matches an HTML-comment demo marker, e.g. `<!-- demo:binding-typed -->` (whitespace-tolerant).
    [GeneratedRegex(@"<!--\s*demo:\s*(?<key>[a-z0-9][a-z0-9-]*)\s*-->")]
    private static partial Regex DemoMarkerRegex();

    // Matches href="…something.md" with an optional "#fragment", excluding absolute/remote URLs.
    [GeneratedRegex("href=\"(?!https?:|/)(?<path>[^\"#]+\\.md)(?<frag>#[^\"]*)?\"")]
    private static partial Regex DocLinkRegex();

    // Markdig fenced-code output: <pre><code class="language-{info}">{HTML-encoded body}</code></pre>.
    // Non-greedy body; the body is HTML-encoded so a literal </code> can never appear inside it.
    [GeneratedRegex("<pre><code class=\"language-(?<lang>[^\"]+)\">(?<body>.*?)</code></pre>", RegexOptions.Singleline)]
    private static partial Regex CodeBlockRegex();
}
