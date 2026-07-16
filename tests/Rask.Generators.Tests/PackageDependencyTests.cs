using System.Xml.Linq;

namespace Rask.Generators.Tests;

// Guards a packaging invariant across the whole repo: a shipped package must never declare a NuGet
// dependency on a project that is never published.
//
// The regression: Rask.Wasm.Hosting referenced Rask.Core (IsPackable=false) without PrivateAssets="all",
// so its nuspec listed `<dependency id="Rask.Core" version="1.0.0" />` — an id that exists on no feed, at
// a version MinVer never stamped. Every restore of the published package died with NU1101, which made the
// wasm-hosted template unusable from NuGet. Nothing caught it: the in-repo build resolves Rask.Core through
// the ProjectReference, so only a restore of the *published* package ever failed.
//
// Unpackable projects reach consumers by being bundled into a host package's lib/ folder (see
// _RaskAddCoreToLib in Rask.Wasm/Rask.Server), which is exactly what PrivateAssets="all" pairs with. This
// test reads the csproj XML rather than packing, so it's instant and covers every package at once.
public class PackageDependencyTests
{
    [Fact]
    public void No_packable_project_depends_on_an_unpackable_one()
    {
        var projects = Directory
            .GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(IsSource)
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p), p => p, StringComparer.OrdinalIgnoreCase);

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

    // Only real sources. Rask.Templates copies its `dotnet new` content — Company.RaskWasm.csproj and friends —
    // into obj/ at build time, so an all-directories scan sees every template project twice and the lookup below
    // throws on the duplicate name rather than reporting anything. Build output is never the thing under test.
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
