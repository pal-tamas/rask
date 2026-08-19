using System.Text.RegularExpressions;

namespace Rask.Example.Shared.Tests.Pages;

/// <summary>
/// Guards that no doc still teaches a routing member the framework has removed.
///
/// <para>Routes moved back to <c>[Route]</c> / <c>[ParentRoute]</c> and the <c>Page</c> base class went away,
/// but a snippet is only prose to the compiler: <c>docs/native-devices.md</c> kept a
/// <c>protected override string Route</c> on a <c>Screen</c> long after the member it overrode was gone, so a
/// reader who copied it got <c>CS0115</c>. That is the worst kind of doc bug — the page reads as
/// authoritative and the reader blames their own code.</para>
///
/// <para>The other doc gates check <em>structure</em> (links resolve, every doc is reachable); this checks
/// that the API the docs name still exists. It is a deliberately small, literal list: the members a breaking
/// change deleted, each with the syntax that replaced it. When the next rename lands, add its pair here and
/// the docs cannot silently rot past it.</para>
/// </summary>
public sealed partial class DocsRoutingSyntaxTests
{
    /// <summary>Each removed member, matched literally, with the syntax that replaced it.</summary>
    private static readonly (Regex Pattern, string Removed, string Replacement)[] RemovedMembers =
    [
        (RouteOverrideRegex(), "protected override string Route", "the [Route(\"...\")] attribute"),
        (ParentOverrideRegex(), "protected override Type? Parent", "the [ParentRoute(typeof(...))] attribute"),
        (PageBaseRegex(), "the Page base class", "a plain Component, or Screen for native chrome"),
    ];

    [Fact]
    public void No_doc_teaches_a_routing_member_that_was_removed()
    {
        var docs = DocsDirectory();
        var stale = new List<string>();

        foreach (var file in Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var source = Path.GetRelativePath(docs, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (pattern, removed, replacement) in RemovedMembers)
                {
                    if (pattern.IsMatch(lines[i]))
                    {
                        stale.Add($"{source}:{i + 1} uses {removed} — use {replacement}");
                    }
                }
            }
        }

        Assert.True(
            stale.Count == 0,
            "These docs name a routing member the framework removed, so a reader who copies the snippet gets "
            + $"a compile error:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", stale));
    }

    [GeneratedRegex(@"\boverride\s+string\s+Route\b")]
    private static partial Regex RouteOverrideRegex();

    [GeneratedRegex(@"\boverride\s+Type\??\s+Parent\b")]
    private static partial Regex ParentOverrideRegex();

    // Only a base list — ": Page" after a type name. `Page` is also an ordinary property name on the data
    // grid ("Set `Page` with `OnPageChange`"), and matching the bare word would fail on those.
    [GeneratedRegex(@"class\s+\w+\s*:\s*Page\b")]
    private static partial Regex PageBaseRegex();

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
}
