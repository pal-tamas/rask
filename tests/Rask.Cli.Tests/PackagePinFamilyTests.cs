using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     The pins in <c>Directory.Packages.props</c> that are only correct in company — families that must
///     move together, and a security hold that only holds while every consumer keeps asking for it.
/// </summary>
/// <remarks>
///     <para>
///         Central package management allows exactly one version per package, which is what makes a family
///         bump atomic within the file. It does NOT make the family bump atomic across the repo: a
///         Dependabot PR bumps whatever it was told to bump, and the packages whose constraint is "the same
///         as that other one" are held together by comments beside the pins. Comments do not fail.
///     </para>
///     <para>
///         These assertions are cheap and offline. They run in the local unit gate, which
///         <c>.githooks/pre-commit</c> triggers on any <c>Directory.</c> path — so the gate that already
///         fires for a version bump is the one that now checks the bump was complete.
///     </para>
/// </remarks>
public sealed class PackagePinFamilyTests
{
    /// <summary>Spectre's testing package is version-locked to the library it fakes.</summary>
    /// <remarks>
    ///     <c>Directory.Packages.props</c> says to keep the pair identical, and Dependabot does not group
    ///     them — so a bump to one opens a PR that leaves the other behind. Spectre's testing package
    ///     reaches into the library's internals; a mismatched pair is a compile error at best.
    /// </remarks>
    [Fact]
    public void Spectre_console_and_its_testing_package_move_together()
    {
        var pins = RepoPins.Packages();

        Assert.Equal(pins["Spectre.Console"], pins["Spectre.Console.Testing"]);
    }

    /// <summary>The two halves of SQLitePCLRaw are one release, and are pinned as one.</summary>
    [Fact]
    public void The_sqlitepclraw_bundle_and_core_move_together()
    {
        var pins = RepoPins.Packages();

        Assert.Equal(pins["SQLitePCLRaw.bundle_e_sqlite3"], pins["SQLitePCLRaw.core"]);
    }

    /// <summary>
    ///     Every project that speaks SQLite can still reach the patched SQLitePCLRaw, so the CVE hold
    ///     actually holds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The 3.x pin exists to escape CVE-2025-6965 in the SQLite that <c>SQLitePCLRaw</c> 2.1.11
    ///         carries. <c>Microsoft.Data.Sqlite</c> and <c>Microsoft.EntityFrameworkCore.Sqlite</c> ask for
    ///         the 2.1.x family, and <c>CentralPackageTransitivePinning</c> is OFF — so the hold works only
    ///         because something in each project's graph asks for 3.x DIRECTLY and NuGet lifts the rest.
    ///     </para>
    ///     <para>
    ///         Nothing enforced that. A new SQLite-touching project that names only
    ///         <c>Microsoft.Data.Sqlite</c> silently resolves the vulnerable family, and the build stays
    ///         green because a downgrade to a transitive default is not a downgrade NuGet reports.
    ///     </para>
    ///     <para>
    ///         Reachability, not a direct reference, is the real invariant: <c>Rask.Logging</c> names
    ///         <c>Microsoft.Data.Sqlite</c> and no <c>SQLitePCLRaw</c>, and is correct today because it
    ///         project-references <c>Rask.SQLite</c>, whose own package dependency demands 3.x of every
    ///         consumer. So the closure is what gets walked.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Every_sqlite_project_can_reach_the_patched_sqlitepclraw()
    {
        var projects = AllProjects();

        var sqliteProjects = projects
            .Where(p => UsesSqlite(p.Value.Packages))
            .Select(p => p.Key)
            .ToList();

        var offenders = sqliteProjects
            .Where(p => !ReachesSqlitePclRaw(p, projects, []))
            .Select(Path.GetFileName)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These projects use SQLite but nothing in their project-reference closure asks for "
            + "SQLitePCLRaw directly, so they resolve the 2.1.x family that CVE-2025-6965 affects:\n  "
            + string.Join("\n  ", offenders)
            + "\nAdd <PackageReference Include=\"SQLitePCLRaw.bundle_e_sqlite3\"/> (central package "
            + "management supplies the version), or reference a project that already does.");

        // A guard that matches nothing would pass by checking nothing — #859's lesson.
        Assert.True(
            sqliteProjects.Count > 5,
            $"The SQLite project scan found {sqliteProjects.Count} projects; csproj discovery is broken.");
    }

    /// <summary>
    ///     The .NET platform stack ships as one versioned family and is pinned as one.
    /// </summary>
    /// <remarks>
    ///     These are exactly the packages <c>.github/dependabot.yml</c> puts in its
    ///     <c>microsoft-extensions</c> group, and grouping them is a statement that they move together. The
    ///     group makes Dependabot open ONE PR; it does not make a hand-edit keep them aligned, and a
    ///     scaffolded project restores against whatever these resolve to.
    /// </remarks>
    [Fact]
    public void The_dotnet_platform_stack_is_on_one_version()
    {
        var family = RepoPins.Packages()
            .Where(p =>
                p.Key.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                || p.Key.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || p.Key.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || p.Key is "Microsoft.Data.Sqlite" or "Microsoft.JSInterop")
            .ToList();

        Assert.True(family.Count > 10, "The platform-stack pin scan found almost nothing.");

        var versions = family.Select(p => p.Value).Distinct(StringComparer.Ordinal).ToList();

        Assert.True(
            versions.Count == 1,
            "The .NET platform stack is split across versions:\n  "
            + string.Join(
                "\n  ",
                family.OrderBy(p => p.Value, StringComparer.Ordinal).Select(p => $"{p.Value}  {p.Key}"))
            + "\nThese ship as one 10.0.x family and dependabot.yml groups them; bring them back to one "
            + "version in Directory.Packages.props.");
    }

    /// <summary>Every csproj in the repo, mapped to the packages and projects it references.</summary>
    private static Dictionary<string, (HashSet<string> Packages, List<string> Projects)> AllProjects()
    {
        var root = CliBuildE2E.FindRepoRoot();
        var map = new Dictionary<string, (HashSet<string> Packages, List<string> Projects)>(StringComparer.Ordinal);

        foreach (var area in new[] { "src", "samples", "tests", "benchmarks" })
        {
            var directory = Path.Combine(root, area);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var csproj in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            {
                var document = XDocument.Load(csproj);
                var packages = document.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference")
                    .Select(e => (string?)e.Attribute("Include"))
                    .Where(i => i is not null)
                    .Select(i => i!)
                    .ToHashSet(StringComparer.Ordinal);

                var projects = document.Descendants()
                    .Where(e => e.Name.LocalName == "ProjectReference")
                    .Select(e => (string?)e.Attribute("Include"))
                    .Where(i => i is not null)
                    .Select(i => Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(csproj)!,
                        i!.Replace('\\', Path.DirectorySeparatorChar))))
                    .ToList();

                map[Path.GetFullPath(csproj)] = (packages, projects);
            }
        }

        return map;
    }

    private static bool UsesSqlite(HashSet<string> packages) =>
        packages.Contains("Microsoft.Data.Sqlite")
        || packages.Contains("Microsoft.EntityFrameworkCore.Sqlite");

    /// <summary>Walks the project-reference closure looking for a direct SQLitePCLRaw reference.</summary>
    private static bool ReachesSqlitePclRaw(
        string project,
        Dictionary<string, (HashSet<string> Packages, List<string> Projects)> projects,
        HashSet<string> seen)
    {
        if (!seen.Add(project) || !projects.TryGetValue(project, out var node))
        {
            return false;
        }

        return node.Packages.Any(p => p.StartsWith("SQLitePCLRaw", StringComparison.Ordinal))
               || node.Projects.Any(r => ReachesSqlitePclRaw(r, projects, seen));
    }
}
