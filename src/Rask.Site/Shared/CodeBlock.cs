namespace Rask.Site;

/// <summary>
///     A snippet to read rather than run — a stylesheet line, a csproj property, a command — drawn as
///     <see cref="CodeSample" /> draws its code pane.
/// </summary>
/// <remarks>
///     A live example is a <see cref="CodeSample" />, whose code is the file that runs. This is for the code that has
///     no result on the page: the text is the page's own, so nothing it highlights came from a reader.
/// </remarks>
internal sealed partial class CodeBlock : Component
{
    /// <summary>The code, as it is to be read.</summary>
    public required string Source { get; set; }

    /// <summary>Its language as a Markdown fence names it: <c>csharp</c>, <c>css</c>, <c>xml</c>, <c>bash</c>.</summary>
    public required string Language { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var language = SyntaxHighlighter.LanguageFor(Language);
        return Pre.Class("code-block my-4 overflow-x-auto rounded-xl border border-zinc-200 dark:border-white/10 bg-(--rask-ink) px-5 py-4 "
                         + "text-[0.82rem] leading-relaxed text-[#e7e3ff]")[
            Code.Class("language-" + Language)[
                language is null
                    ? Text.Value(Source.TrimEnd())
                    : Raw.Value(SyntaxHighlighter.Highlight(Source, language))
            ]
        ];
    }
}
