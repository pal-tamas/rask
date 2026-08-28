using ColorCode;
using ColorCode.Common;

namespace Rask.Example.Shared;

// A custom ColorCode language for the scoped `.ts` files the demos show beside their C#.
//
// ColorCode.Core ships a JavaScript lexer but no TypeScript one, and aliasing `.ts` onto JavaScript
// was the obvious shortcut — it is wrong here for a specific reason. Every type annotation would
// render as an undistinguished identifier, on precisely the pages that exist to teach people to
// write type annotations. `interface`, `readonly` and `satisfies` would read as plain words.
//
// Same shape and the same precedent as BashLanguage: rule order IS precedence, so strings and
// comments come first and are never re-lexed as keywords. Scope names map to the .comment / .string
// / .keyword / .className / .number token CSS the other languages already use, so a TypeScript pane
// sits beside a C# pane without either looking foreign.
internal sealed class TypeScriptLanguage : ILanguage
{
    public string Id => "typescript";

    public string Name => "TypeScript";

    public string CssClassName => "typescript";

    public string? FirstLinePattern => null;

    public IList<LanguageRule> Rules { get; } =
    [
        // Comments first, so a `//` inside neither a string nor a URL is the only thing that starts one.
        new LanguageRule(
            @"/\*[\s\S]*?\*/",
            new Dictionary<int, string> { { 0, ScopeName.Comment } }),
        new LanguageRule(
            @"(//.*?)(?=\r|\n|$)",
            new Dictionary<int, string> { { 1, ScopeName.Comment } }),

        // Strings, including template literals — the demos use them for URLs and interpolation, and
        // without this rule a `${…}` inside one lexes as punctuation and keywords.
        new LanguageRule(
            @"`(?:[^`\\]|\\.)*`",
            new Dictionary<int, string> { { 0, ScopeName.String } }),
        new LanguageRule(
            @"'(?:[^'\\\r\n]|\\.)*'",
            new Dictionary<int, string> { { 0, ScopeName.String } }),
        new LanguageRule(
            "\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"",
            new Dictionary<int, string> { { 0, ScopeName.String } }),

        // The TypeScript-only keywords, listed before the shared JavaScript ones purely so this set
        // is visible as the thing that makes the language what it is.
        new LanguageRule(
            @"\b(interface|type|implements|declare|namespace|abstract|readonly|as|satisfies|keyof|infer|is|asserts|enum|public|private|protected|override|out)\b",
            new Dictionary<int, string> { { 1, ScopeName.Keyword } }),

        new LanguageRule(
            @"\b(async|await|break|case|catch|class|const|continue|default|delete|do|else|export|extends|finally|for|from|function|get|if|import|in|instanceof|let|new|of|return|set|static|super|switch|this|throw|try|typeof|var|void|while|yield)\b",
            new Dictionary<int, string> { { 1, ScopeName.Keyword } }),

        new LanguageRule(
            @"\b(true|false|null|undefined)\b",
            new Dictionary<int, string> { { 1, ScopeName.Keyword } }),

        // The primitive type names, which are what most annotations in these demos are made of.
        new LanguageRule(
            @"\b(string|number|boolean|bigint|symbol|object|any|unknown|never|Promise|Array|Record|Map|Set|WeakMap|Date)\b",
            new Dictionary<int, string> { { 1, ScopeName.ClassName } }),

        // A capitalised identifier — a DOM type (HTMLElement) or one the demo declares itself. The
        // pattern is a heuristic and cannot be otherwise without a parser; over-colouring a
        // constructor call reads fine, and under-colouring every annotation does not.
        new LanguageRule(
            @"\b([A-Z][A-Za-z0-9_]*)\b",
            new Dictionary<int, string> { { 1, ScopeName.ClassName } }),

        new LanguageRule(
            @"\b(\d+(?:\.\d+)?)\b",
            new Dictionary<int, string> { { 1, ScopeName.Number } }),
    ];

    public bool HasAlias(string lang) => lang.ToLowerInvariant() is "ts" or "typescript";
}
