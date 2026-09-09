using ColorCode;
using ColorCode.Common;

namespace Rask.Site;

// A ColorCode language for the island front ends: .tsx and .jsx, and the single-file components
// (.vue, .svelte) that are the same thing with the markup on the outside.
//
// These files had NO lexer at all. SyntaxHighlighter.LanguageFor returned null for every one of them,
// which the callers render as plain encoded text — so the islands guide, the page whose entire subject
// is writing a React or Svelte component, showed its source as an undifferentiated grey block while the
// C# pane beside it was fully coloured. Nothing reported it: "no lexer" and "a lexer that found nothing"
// produce identical markup.
//
// It is TypeScriptLanguage's rules with element tags inserted, and the insertion POINT is the whole
// design. Rule order is precedence in ColorCode, so:
//
//   - comments and strings first, or `const html = "<div>"` colours a tag inside a string literal;
//   - then the tag rules, or `<Foo` has already been consumed as an identifier by the keyword pass;
//   - then the language itself.
//
// That is why TypeScriptLanguage exposes CommentsAndStrings and Keywords separately rather than one
// Rules list — this file has to get between them, and copying its patterns here would be two
// definitions of TypeScript drifting apart.
//
// Tags map to ScopeName.ClassName and attribute names to ScopeName.Keyword deliberately: those two
// token classes are already styled in global.css for every other language, so a .tsx pane sits beside
// a .cs pane in the same palette. ColorCode's own html* scope names would have needed a parallel set of
// CSS rules that do not exist, which is the failure mode this file was written to end.
internal sealed class TsxLanguage : ILanguage
{
    public string Id => "tsx";

    public string Name => "TSX";

    public string CssClassName => "tsx";

    public string? FirstLinePattern => null;

    public IList<LanguageRule> Rules { get; } =
    [
        .. TypeScriptLanguage.CommentsAndStrings,

        // An element tag: <div, </div, <Counter, </Counter, <svelte:head. Captures the NAME only, so
        // the angle brackets stay punctuation and read as the quieter part of the line.
        new LanguageRule(
            @"</?([A-Za-z][A-Za-z0-9._:-]*)",
            new Dictionary<int, string> { { 1, ScopeName.ClassName } }),

        // An attribute name — anything immediately followed by `=`. Covers JSX (onClick={…}),
        // Vue (:prop, @click, v-if) and Svelte (on:click, bind:value) without a rule each, because
        // all of them are "a name, then an equals sign".
        new LanguageRule(
            @"([@:A-Za-z_][-A-Za-z0-9_:.]*)(?=\s*=)",
            new Dictionary<int, string> { { 1, ScopeName.Keyword } }),

        .. TypeScriptLanguage.Keywords,
    ];

    // `jsx` and `tsx` are the fence info-strings the guides actually write; vue and svelte come here
    // too. An SFC is markup wrapped around a script block, and these rules cover both halves — its
    // <style> block is the one part they read loosely, which is a better answer than the grey block
    // that was there before and does not need a second parser to be true.
    public bool HasAlias(string lang) =>
        lang.ToLowerInvariant() is "tsx" or "jsx" or "vue" or "svelte";
}
