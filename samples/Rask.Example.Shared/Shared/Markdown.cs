using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Markdig;

namespace Rask.Example.Shared;

// A reusable Markdown renderer: pass it markdown Source and it renders to HTML with Markdig and injects
// the result via Raw() inside a .markdown-body wrapper (styled globally in wwwroot/global.css — the Raw
// HTML carries no scope id, so those prose rules can't be component-scoped). The rendered HTML is cached
// per source, so Markdig parses once. This is the showcase's prose component; it also rewrites the docs'
// relative cross-links so they work in the SPA: a link to another guide ("foo.md", optionally with a
// "#frag" or "dir/" prefix) becomes a SPA-routed /guides/{leaf} anchor (data-rask-nav), and a link up to
// the repo root (../README.md) points at GitHub.
public sealed partial class Markdown : Component
{
    // AutoIdentifiers gives every heading a stable id (anchor links); the advanced extensions cover
    // tables, fenced code, task lists, etc. — the Markdown the guides actually use. Thread-safe, reused.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers()
        .Build();

    private static readonly ConcurrentDictionary<string, string> HtmlCache = new(StringComparer.Ordinal);

    // Non-nullable + no initializer ⇒ the factory generator emits Source as a required positional
    // parameter (mirrors CodeSample.Files). Rask assigns it after construction, so CS8618 is expected.
#pragma warning disable CS8618
    public string Source { get; set; }
#pragma warning restore CS8618

    protected override RenderResult Render() =>
        Div(Class: "markdown-body")[Raw(HtmlCache.GetOrAdd(Source, Render))];

    private static string Render(string source) =>
        RewriteLinks(global::Markdig.Markdown.ToHtml(source, Pipeline));

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

    // Matches href="…something.md" with an optional "#fragment", excluding absolute/remote URLs.
    [GeneratedRegex("href=\"(?!https?:|/)(?<path>[^\"#]+\\.md)(?<frag>#[^\"]*)?\"")]
    private static partial Regex DocLinkRegex();
}
