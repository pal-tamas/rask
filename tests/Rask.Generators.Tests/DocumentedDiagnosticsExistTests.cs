using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Rask.Generators.Tests;

/// <summary>
///     Every id <c>docs/diagnostics.md</c> advertises as ACTIVE has a descriptor behind it.
/// </summary>
/// <remarks>
///     <para>
///         This gate did not exist, and RASK045 spent its life in the gap: documented with a severity, a
///         full section, a code example and suppression instructions — and no descriptor anywhere in
///         <c>src/</c>, so it never fired once (#1051).
///     </para>
///     <para>
///         A documented diagnostic that cannot fire is worse than an undocumented gap. Someone reads the
///         rule, believes the compiler enforces it, writes the pattern, and nothing objects — and they
///         cannot tell that from a codebase with no violations. RASK034 spent its whole life that way
///         too, silently dead after the grid moved to a chain, and nothing noticed until it was retired.
///     </para>
///     <para>
///         The existing checks all run the other way: they start from a descriptor and ask whether it is
///         documented. Nothing asked whether a documented rule exists. Both directions are needed,
///         because each catches what the other cannot.
///     </para>
/// </remarks>
public class DocumentedDiagnosticsExistTests
{
    [Fact]
    public void EveryActiveIdInTheDocsHasADescriptorInSrc()
    {
        var root = RepoRoot();
        var docs = File.ReadAllText(Path.Combine(root, "docs", "diagnostics.md"));

        // The table of contents is the promise: `| [RASK045](#rask045) | Warning | … |`. A retired id
        // carries an em dash where the severity goes, which is exactly how RASK030 and RASK034 are
        // recorded, so reading the severity column separates the two without a second list to maintain.
        var active = Regex
            .Matches(docs, @"^\|\s*\[(?<id>RASK[A-Z]*\d+)\]\(#[a-z0-9]+\)\s*\|\s*(?<severity>[^|]+?)\s*\|",
                RegexOptions.Multiline)
            .Select(m => (Id: m.Groups["id"].Value, Severity: m.Groups["severity"].Value))
            .Where(row => row.Severity is "Error" or "Warning" or "Hidden" or "Info")
            .Select(row => row.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(active.Count > 40, $"only {active.Count} active ids parsed out of the TOC — the "
                                       + "table's shape changed and this gate is no longer reading it.");

        var declared = DeclaredIds(Path.Combine(root, "src"));

        var missing = active.Where(id => !declared.Contains(id)).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "docs/diagnostics.md lists these as ACTIVE diagnostics, but no descriptor for them exists in "
            + "src/ — so they can never fire, and a reader cannot tell that from a codebase with no "
            + "violations:\n  " + string.Join("\n  ", missing)
            + "\n\nEither implement the rule or mark it *Retired* in the table, the way RASK030 is.");
    }

    // Every id a descriptor is constructed with. Read as string literals rather than by loading the
    // analyzer assemblies: FOUR assemblies allocate in this space and the ids are what they agree on.
    private static HashSet<string> DeclaredIds(string srcRoot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in RepoFiles.EnumerateSourceFiles(srcRoot).Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))
        {
            // obj/ and bin/ hold copies of the same sources plus generated files; counting them would
            // make a deleted descriptor look present until someone cleaned.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(RASK[A-Z]*\\d+)\""))
            {
                ids.Add(m.Groups[1].Value);
            }
        }

        // The build layers declare their own ids in MSBuild rather than in C#.
        foreach (var file in RepoFiles.EnumerateSourceFiles(srcRoot).Where(f => f.EndsWith(".targets", StringComparison.Ordinal)))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(RASK[A-Z]*\\d+)\""))
            {
                ids.Add(m.Groups[1].Value);
            }
        }

        return ids;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
