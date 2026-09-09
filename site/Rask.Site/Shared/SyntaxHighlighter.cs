using System.Collections.Concurrent;
using ColorCode;

namespace Rask.Site;

// Server-side syntax highlighting shared by CodeSample (the demo source panes) and Markdown (the guide
// prose ```code fences```). ColorCode tokenizes source into <span class="keyword|string|comment|…">
// markup coloured by the .sample-code / .markdown-body pre token rules in wwwroot/global.css; the result
// is injected via Raw() (ColorCode already HTML-encodes token text), so no client JS runs and the
// highlight is present on the very first render. Memoised per (language, trimmed source) so repeated
// renders and tab switches never re-run the parser.
internal static class SyntaxHighlighter
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    // ColorCode ships no shell lexer, so register our own (see BashLanguage) once, up front.
    private static readonly ILanguage Shell = new BashLanguage();

    // Nor a TypeScript one. Aliasing .ts onto JavaScript would render every type annotation as an
    // undistinguished identifier, on the very pages that teach people to write them.
    private static readonly ILanguage TypeScript = new TypeScriptLanguage();

    // Nor anything for the island front ends. .tsx / .jsx / .vue / .svelte all fell through to null and
    // rendered as flat grey text — on the islands guide, whose whole subject is those files.
    private static readonly ILanguage Tsx = new TsxLanguage();

    static SyntaxHighlighter()
    {
        Languages.Load(Shell);
        Languages.Load(TypeScript);
        Languages.Load(Tsx);
    }

    // Maps a file extension (".cs") OR a markdown fence info-string ("csharp"/"bash"/"html"…) to a
    // ColorCode language, or null when we don't tokenize it (rendered as plain, HTML-encoded text by the
    // caller). Guides also use `razor`/`jsonc`, which ColorCode has no lexer for — they stay plain.
    public static ILanguage? LanguageFor(string key) => key.ToLowerInvariant() switch
    {
        ".ts" or "ts" or "typescript" => TypeScript,
        // The island dialects. A .vue / .svelte single-file component is markup wrapped around a
        // script block, which is what TsxLanguage lexes — see the note there on why one language
        // covers all four rather than four near-identical ones.
        ".tsx" or "tsx" or ".jsx" or "jsx" => Tsx,
        ".vue" or "vue" => Tsx,
        ".svelte" or "svelte" => Tsx,
        ".js" or "js" or "javascript" => Languages.JavaScript,
        ".css" or "css" => Languages.Css,
        ".cs" or "cs" or "csharp" or "c#" => Languages.CSharp,
        ".html" or ".htm" or "html" or "htm" or "xml" => Languages.Html,
        ".sh" or "sh" or "bash" or "shell" or "zsh" or "console" => Shell,
        _ => null,
    };

    public static string Highlight(string source, ILanguage language)
    {
        var trimmed = source.TrimEnd();
        return Cache.GetOrAdd(
            // Key by language id too: the same text tokenizes differently per language.
            $"{language.Id}\n{trimmed}",
            // A fresh formatter per cache-miss: HtmlClassFormatter mutates a per-instance Writer field
            // during GetHtmlString and is therefore not safe to share across concurrent renders. The
            // cache bounds this to one allocation per distinct key.
            _ => StripWrapper(new HtmlClassFormatter().GetHtmlString(trimmed, language)));
    }

    // ColorCode wraps its output as <div class="{lang}"><pre>\n …spans… \n</pre></div>. Callers keep
    // their own <pre><code> for layout and scoped styling, so peel ColorCode's wrapper and return only
    // the inner token spans.
    private static string StripWrapper(string html)
    {
        const string open = "<pre>";
        const string close = "</pre>";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        var end = html.LastIndexOf(close, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            return html; // defensive: ColorCode's output shape changed — render it as-is.
        }

        start += open.Length;
        return html.Substring(start, end - start);
    }
}
