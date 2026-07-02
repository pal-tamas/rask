using ColorCode;
using ColorCode.Common;

namespace Rask.Example.Shared;

// A small custom ColorCode language for shell / bash fences (the guides' `dotnet new …` etc. blocks).
// ColorCode.Core ships no shell lexer, so we define one: comments, quoted strings, and the handful of
// commands/keywords the guides actually use. The scope names (Comment / String / Keyword) map to the
// same .comment / .string / .keyword token CSS the C#/JS/CSS blocks use, so shell reads consistently.
// Rule order is precedence order — strings and comments before keywords, so a `#`-comment or a quoted
// literal is never re-lexed as a command.
internal sealed class BashLanguage : ILanguage
{
    public string Id => "shell";
    public string Name => "Shell";
    public string CssClassName => "shell";
    public string? FirstLinePattern => null;

    public IList<LanguageRule> Rules { get; } =
    [
        new LanguageRule(
            @"'[^'\r\n]*'",
            new Dictionary<int, string> { { 0, ScopeName.String } }),
        new LanguageRule(
            "\"[^\"\\r\\n]*\"",
            new Dictionary<int, string> { { 0, ScopeName.String } }),
        new LanguageRule(
            @"(#.*?)(?=\r|\n|$)",
            new Dictionary<int, string> { { 1, ScopeName.Comment } }),
        new LanguageRule(
            @"\b(dotnet|rask|npm|npx|node|git|cd|export|sudo|echo|cat|curl|mkdir|cp|mv|rm|if|then|else|elif|fi|for|while|do|done|in|case|esac|function)\b",
            new Dictionary<int, string> { { 1, ScopeName.Keyword } }),
    ];

    public bool HasAlias(string lang) => lang.ToLowerInvariant() is "bash" or "sh" or "shell" or "zsh" or "console";
}
