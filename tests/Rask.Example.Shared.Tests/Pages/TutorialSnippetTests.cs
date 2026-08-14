using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Core;

namespace Rask.Example.Shared.Tests.Pages;

/// <summary>
/// Guards the C# the tutorial tells a reader to type.
/// </summary>
/// <remarks>
/// The tutorial used to be a list of <c>rask generate</c> commands, so its code was proved by running the
/// generator and compiling the result. Now the code <em>is</em> the tutorial, and prose does not compile —
/// a snippet can rot into something that never builds and nothing says so until a reader types it out.
/// <para>
/// These are the two cheapest checks that catch the ways it actually breaks. A snippet that no longer
/// parses (a truncated paste, an unbalanced brace) fails the first. A snippet that calls a framework
/// member which does not exist fails the second — the case that prompted this: an early draft of chapter
/// 2 wrote <c>OnInitializedAsync</c>, a name from a different framework, where Rask's lifecycle hook is
/// <c>OnMountAsync</c>. It parsed perfectly and would never have compiled.
/// </para>
/// <para>
/// Deliberately not a full compile. That needs the source generator's global usings, the whole reference
/// closure, and stubs for every type a snippet mentions but does not define — a harness large enough to
/// rot on its own. These two catch the realistic failures at a fraction of the surface.
/// </para>
/// </remarks>
public sealed partial class TutorialSnippetTests
{
    [Fact]
    public void Every_csharp_snippet_parses()
    {
        var broken = new List<string>();

        foreach (var (source, code) in Snippets().Where(s => !IsElided(s.Code)))
        {
            // The tutorial shows whole files, single members, and bare statements. Rather than guess which
            // from the text, try each shape: a snippet is fine if it parses as any of them, and broken only
            // if it parses as none. Guessing was worse than useless here — "contains the word record" read
            // a member fragment as a file and reported correct docs as broken.
            if (Parses(code)                                              // a whole file
                || Parses($"class Wrap {{ {code} }}")                      // a member
                || Parses($"class Wrap {{ void M() {{ {code} }} }}")       // statements
                || Parses($"class Wrap {{ void M() {{ _ = new T {{ {code} }}; }} }}"))   // initializer entries
            {
                continue;
            }

            var firstError = CSharpSyntaxTree
                .ParseText(code, new CSharpParseOptions(LanguageVersion.Latest))
                .GetDiagnostics()
                .First(d => d.Severity == DiagnosticSeverity.Error);

            broken.Add($"{source}: {firstError.GetMessage()}");
        }

        Assert.True(
            broken.Count == 0,
            "These tutorial snippets don't parse as C#. A reader copying one gets a compiler error before "
            + $"they get anywhere:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", broken));
    }

    [Fact]
    public void Every_overridden_member_exists_on_the_component_base()
    {
        // Everything a Rask page overrides is declared on Component. A name that isn't there is a member
        // from another framework, or one that has since been renamed.
        var overridable = Overridable(typeof(Component));
        var unknown = new List<string>();

        // Only snippets that declare a Component. The tutorial also overrides EF Core's DbContext
        // (OnModelCreating), and those names are not Component members — checking every `override` in the
        // whole tutorial against one base type would report correct code as broken.
        foreach (var (source, code) in Snippets().Where(s => s.Code.Contains(": Component", StringComparison.Ordinal)))
        {
            foreach (Match match in OverrideDeclaration().Matches(code))
            {
                var name = match.Groups["name"].Value;
                if (!overridable.Contains(name))
                {
                    unknown.Add($"{source}: override {name}");
                }
            }
        }

        Assert.True(
            unknown.Count == 0,
            "These snippets override a member that doesn't exist on Rask's Component — the name is from "
            + $"another framework, or it was renamed:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", unknown.Distinct(StringComparer.Ordinal)));
    }

    /// <summary>Every fenced C# block in the tutorial, with the chapter it came from.</summary>
    private static IEnumerable<(string Source, string Code)> Snippets()
    {
        var tutorial = Path.Combine(DocsDirectory(), "tutorial");
        foreach (var file in Directory.EnumerateFiles(tutorial, "*.md").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in CSharpFence().Matches(text))
            {
                // A fence nested in a blockquote carries the "> " on every line. That is Markdown, not C#,
                // and parsing it as C# reports the quote marks as the syntax error.
                var code = BlockquotePrefix().Replace(match.Groups["code"].Value, string.Empty);
                yield return (Path.GetFileName(file), code);
            }
        }
    }

    /// <summary>Every virtual/overridable member name on <paramref name="type"/> and its bases.</summary>
    private static HashSet<string> Overridable(Type type) =>
        type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m is MethodInfo { IsVirtual: true } or PropertyInfo)
            .Select(m => m.Name.StartsWith("get_", StringComparison.Ordinal) ? m.Name[4..] : m.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// True when the snippet elides code with <c>…</c>. That character is the docs' "the rest is not the
    /// point here" marker, and a sketch like <c>class CreateProduct : Component { … }</c> is deliberately
    /// not something to copy — holding it to the parser would only push the prose into contortions.
    /// </summary>
    private static bool IsElided(string code) => code.Contains('…', StringComparison.Ordinal);

    /// <summary>True when <paramref name="code"/> parses with no syntax errors.</summary>
    private static bool Parses(string code) =>
        !CSharpSyntaxTree
            .ParseText(code, new CSharpParseOptions(LanguageVersion.Latest))
            .GetDiagnostics()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

    private static string DocsDirectory()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return Path.Combine(dir, "docs");
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }

    [GeneratedRegex(@"```csharp\r?\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpFence();

    [GeneratedRegex(@"^> ?", RegexOptions.Multiline)]
    private static partial Regex BlockquotePrefix();

    /// <summary>
    /// The member name in an <c>override</c> declaration — the last identifier before the parameter list
    /// or the expression body.
    /// <para>
    /// Written to skip everything between <c>override</c> and that name rather than to match a return
    /// type, because modifiers live in there: an earlier version expected
    /// <c>override &lt;type&gt; &lt;name&gt;(</c> and so matched nothing at all on
    /// <c>override async Task OnMountAsync()</c> — quietly checking none of the async overrides, which is
    /// most of them. A guard that silently matches nothing is worse than no guard.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"\boverride\s+[^\r\n;{(=]*?(?<name>\w+)\s*(?=\(|=>)")]
    private static partial Regex OverrideDeclaration();
}
