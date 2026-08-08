using System.Xml.Linq;

namespace Rask.Generators.Tests;

// Guards the two packaging invariants that hold across the whole repo:
//   1. a shipped package must never declare a NuGet dependency on a project that is never published;
//   2. a project that declares itself packable must actually be packed by the publishing workflows.
//
// Both are the same class of bug — the package a consumer needs isn't on the feed — and neither shows up
// in an in-repo build, which resolves everything through ProjectReferences.
//
// The regression: Rask.Wasm.Hosting referenced Rask.Core (IsPackable=false) without PrivateAssets="all",
// so its nuspec listed `<dependency id="Rask.Core" version="1.0.0" />` — an id that exists on no feed, at
// a version MinVer never stamped. Every restore of the published package died with NU1101, which made a
// wasm-hosted app (which references Rask.Wasm.Hosting) unrestorable from NuGet. Nothing caught it: the
// in-repo build resolves Rask.Core through the ProjectReference, so only a restore of the *published* package failed.
//
// Unpackable projects reach consumers by being bundled into a host package's lib/ folder (see
// _RaskAddCoreToLib in Rask.Wasm/Rask.Server), which is exactly what PrivateAssets="all" pairs with. This
// test reads the csproj XML rather than packing, so it's instant and covers every package at once.
public class PackageDependencyTests
{
    [Fact]
    public void No_packable_project_depends_on_an_unpackable_one()
    {
        var projects = SourceProjects();

        var offenders = new List<string>();

        foreach (var (name, path) in projects.Where(p => IsPackable(p.Value)))
        {
            var doc = XDocument.Load(path);
            foreach (var reference in doc.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (include is null)
                {
                    continue;
                }

                var target = Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
                // Only a reference to an unpackable project can name a nonexistent package.
                if (!projects.TryGetValue(target, out var targetPath) || IsPackable(targetPath))
                {
                    continue;
                }

                // ReferenceOutputAssembly="false" already keeps the reference out of the nuspec — it's how the
                // analyzer/generator projects are consumed (OutputItemType="Analyzer"), and those pack fine.
                if (string.Equals(reference.Attribute("ReferenceOutputAssembly")?.Value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // PrivateAssets="all" is what keeps it out of the nuspec's dependency list.
                var privateAssets = reference.Attribute("PrivateAssets")?.Value
                    ?? reference.Element(reference.Name.Namespace + "PrivateAssets")?.Value;

                if (!string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{name} -> {target} (PrivateAssets=\"{privateAssets ?? "<unset>"}\")");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These packable projects reference an IsPackable=false project without PrivateAssets=\"all\", so their "
            + "nuspec will declare a dependency on a package that does not exist and every restore of them fails "
            + "with NU1101:\n  " + string.Join("\n  ", offenders));
    }

    // The list of `dotnet pack` steps in the publishing workflows is maintained by hand, and nothing tied it to
    // the projects that actually declare themselves packable. Rask.Postgres and Rask.SqlServer were added with a
    // PackageId, a Description and their own NUGET.md — everything a shipped package has except a pack step — so
    // they were built, tested and documented (docs/databases.md links their nuget.org page, and `rask new
    // --database postgres` scaffolds a PackageReference to them) while existing on no feed at all.
    //
    // A missing step in release.yml means the package is never published; a missing one in nightly.yml means it
    // is never smoke-tested from a feed before the release. Matching on the csproj path is what both workflows
    // are written in terms of, and it covers Rask.Native too, which each packs from a separate macOS job.
    [Theory]
    [InlineData("release.yml")]
    [InlineData("nightly.yml")]
    public void Every_packable_project_is_packed_by_the_publishing_workflow(string workflow)
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", workflow));

        var missing = SourceProjects()
            .Where(p => IsPackable(p.Value))
            .Select(p => Path.GetRelativePath(RepoRoot(), p.Value).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relativePath => !yaml.Contains(relativePath, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These projects are IsPackable but no `dotnet pack` step in .github/workflows/{workflow} names them, "
            + "so the package they describe is produced by nobody — a consumer following the docs gets NU1101:\n  "
            + string.Join("\n  ", missing));
    }

    // NUGET.md is packed into EVERY package (Directory.Build.props), so it is the most-read page the project
    // ships and the one nobody edits — it listed 7 packages while the repo built 24, missing every battery
    // (Jobs/Mail/Cache/Outbox/Data/Dashboard/Logging), both SQLite satellites, both alternative database
    // providers and Rask.Testing (#602). A package that exists and is documented nowhere is one a reader can
    // only find by reading the source, which defeats the point of shipping it.
    //
    // Matching on the PackageId is deliberate: the id is what a reader types into `dotnet add package`, so
    // naming it is the minimum bar. This does not check that what NUGET.md SAYS about a package is right —
    // nothing can — only that the package is not silently absent.
    [Fact]
    public void Every_packable_project_is_named_in_the_packed_readme()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "NUGET.md"));

        var missing = SourceProjects()
            .Where(p => IsPackable(p.Value))
            .Select(p => PackageId(p.Value))
            .Where(id => id is not null && !readme.Contains(id, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These packages ship but NUGET.md — which is packed into every one of them — never names them, so "
            + "the most-read page the project publishes does not admit they exist:\n  "
            + string.Join("\n  ", missing));
    }

    // The <PackageId> a project publishes under, or its file name when it doesn't override one.
    private static string? PackageId(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        var declared = doc.Descendants("PackageId").FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(declared) || declared.Contains('$', StringComparison.Ordinal)
            ? Path.GetFileNameWithoutExtension(csprojPath)
            : declared;
    }

    // Every project under src/, packable or not, keyed by its file name. Both invariants above need the whole set:
    // one to tell a reference to an unpackable project from a reference to a shipped one, the other to pick the
    // packable ones out.
    private static Dictionary<string, string> SourceProjects() =>
        Directory
            .GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(IsSource)
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p), p => p, StringComparer.OrdinalIgnoreCase);

    // Only real sources — never anything a build copied under bin/ or obj/. An all-directories scan can otherwise
    // pick up staged/intermediate csproj copies and see a project twice, so the lookup below would throw on the
    // duplicate name rather than report anything. Build output is never the thing under test.
    private static bool IsSource(string csprojPath) =>
        !csprojPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    // Mirrors how the repo declares it: an explicit <IsPackable>false</IsPackable>. Everything else in src/
    // ships (the SDK default is true), so absence means packable.
    private static bool IsPackable(string csprojPath) =>
        !XDocument.Load(csprojPath)
            .Descendants("IsPackable")
            .Any(e => string.Equals(e.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    // Walks up from the test assembly to the repo root (the directory holding Rask.slnx).
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate repo root (Rask.slnx) from " + AppContext.BaseDirectory);
    }
}
