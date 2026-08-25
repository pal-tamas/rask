using System.Text.Json;
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
    // the projects that actually declare themselves packable. A package can otherwise be added with a
    // PackageId, a Description and its own NUGET.md — everything a shipped package has except a pack step —
    // and so be built, tested and documented while existing on no feed at all (#602).
    //
    // A missing step in release.yml means the package is never published; a missing one in nightly.yml means it
    // is never smoke-tested from a feed before the release. Matching on the csproj path is what both workflows
    // are written in terms of.
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

    // A package that ships its OWN NUGET.md must Include it, never Update it.
    //
    // The regression this pins: `<None Update="NUGET.md" Pack="true" …>` relies on the file already being in
    // the None collection, which it only is in the INNER (per-TargetFramework) build — the SDK's default item
    // globs do not run in the outer build. Pack runs on the outer build, so for a MULTI-TARGETED project the
    // Update matched nothing, while the neighbouring `<None Remove="..\..\NUGET.md" />` still stripped the
    // repo-root readme Directory.Build.props contributes. The package came out with no readme at all and pack
    // failed with NU5039 — which broke every nightly publish, silently, because a plain `dotnet build` never
    // touches the outer-build pack path.
    //
    // Include is correct for single- and multi-targeted projects alike, so the rule is uniform rather than
    // "Update is fine until you add a second TFM".
    [Fact]
    public void A_package_with_its_own_readme_includes_it_rather_than_updating_it()
    {
        var offenders = SourceProjects()
            .Where(p => IsPackable(p.Value))
            .Where(p => File.ReadAllText(p.Value).Contains("<None Update=\"NUGET.md\"", StringComparison.Ordinal))
            .Select(p => p.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These projects Update NUGET.md instead of Including it, so their package ships without a readme "
            + "and pack fails with NU5039 on a multi-targeted project:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
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

    [Fact]
    public void A_bundled_dll_ships_its_XML_docs_too()
    {
        // Rask.Core and Rask.Client are IsPackable=false and reach consumers by being copied into a host
        // package's lib/ folder. Their XML doc file has to make the same trip: it is the ONLY way the
        // documentation on Component, Element and every element component reaches anyone consuming the
        // package. Drop the line and nothing breaks — the build is green, the API works, and every
        // tooltip in the consumer's IDE is silently blank. The factory generator suffers twice over,
        // because it reads those same summaries to put a <param> on each generated factory.
        //
        // Scoped to lib/ deliberately. A DLL packed to analyzers/dotnet/cs is loaded by the compiler, never
        // referenced by user code, so it has no API surface a consumer's IDE could show docs for — the
        // generator packages (Rask.Cqrs.Generators, Rask.Jobs.Generators, Rask.Outbox.Generators) ship
        // without an .xml on purpose, and demanding one there would be noise, not a guard.
        var offenders = new List<string>();

        foreach (var (name, path) in SourceProjects().Where(p => IsPackable(p.Value)))
        {
            var packed = XDocument.Load(path)
                .Descendants()
                .Where(e => e.Name.LocalName is "None" or "TfmSpecificPackageFile")
                .Where(e => e.Attribute("PackagePath")?.Value.Replace('\\', '/')
                    .StartsWith("lib", StringComparison.OrdinalIgnoreCase) == true)
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => v is not null)
                .Select(v => v!.Replace('\\', '/'))
                .ToList();

            foreach (var dll in packed.Where(v => v.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                var xml = dll[..^4] + ".xml";
                if (!packed.Contains(xml, StringComparer.OrdinalIgnoreCase))
                {
                    offenders.Add($"{name} packs {Path.GetFileName(dll)} without {Path.GetFileName(xml)}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A bundled DLL must ship its XML docs beside it, or the package's IntelliSense is blank: "
            + string.Join("; ", offenders));
    }

    // The mirror image of the first invariant, and the half that was missing.
    //
    // An unpackable project reaches consumers by having its DLL copied into a host package's lib/ folder.
    // That copy carries the assembly and nothing else: NuGet never learns what the bundled project itself
    // depended on, because PrivateAssets="all" is precisely what keeps it out of the nuspec. So every
    // package the bundled assembly needs at runtime has to be re-declared at the HOST's boundary, or the
    // consumer restores a package whose code calls into an assembly that was never downloaded.
    //
    // The regression: Rask.Wasm bundles Rask.Core.dll and re-declared Microsoft.JSInterop and
    // Microsoft.AspNetCore.Authorization for exactly this reason, but not Microsoft.Extensions.ObjectPool
    // — which RaskStringBuilderPool.Shared uses on the render path (Component.cs, Live/LivePayload.cs,
    // HeadAssets/HeadAssetRegistry.cs). Nothing caught it: an in-repo build resolves everything through the
    // ProjectReference, so ObjectPool is always present locally and only a restore of the PUBLISHED package
    // is missing it. Rask.Server is immune because Microsoft.AspNetCore.App carries ObjectPool; the WASM
    // track has no framework reference to hide behind. That is #742.
    //
    // "Declared" deliberately includes reachable-transitively, read from the host's own restore graph
    // rather than assumed: Microsoft.Extensions.Primitives sits in the same position as ObjectPool and is
    // fine only because Logging -> Options -> Primitives brings it in. Reading the real graph is what makes
    // this fail if that edge ever disappears — a hand-maintained "covered transitively" list would not.
    [Fact]
    public void A_bundled_projects_package_dependencies_are_declared_at_the_host_boundary()
    {
        var projects = SourceProjects();
        var offenders = new List<string>();

        foreach (var (name, path) in projects.Where(p => IsPackable(p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var document = XDocument.Load(path);

            var bundled = BundledProjects(document, projects);
            if (bundled.Count == 0)
            {
                continue;
            }

            // A framework reference carries a whole shared framework, which is where Rask.Server gets
            // ObjectPool, Primitives and JSInterop from. Naming the assemblies inside it would mean
            // hard-coding a list that ships with the SDK, so the presence of the reference is the answer.
            if (document.Descendants("FrameworkReference")
                .Any(e => e.Attribute("Include")?.Value == "Microsoft.AspNetCore.App"))
            {
                continue;
            }

            var declared = PackageReferences(document);
            var needed = bundled
                .SelectMany(b => TransitivePackageReferences(b, projects))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => !declared.Contains(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (needed.Count == 0)
            {
                continue;
            }

            var reachable = ReachablePackagesPerTarget(path, name);
            foreach (var package in needed)
            {
                // Every target framework the host ships gets its own nuspec dependency group, so a package
                // reachable in one and not another is still a hole in that one.
                var uncovered = reachable
                    .Where(target => !target.Value.Contains(package))
                    .Select(target => target.Key)
                    .ToList();

                if (uncovered.Count > 0)
                {
                    offenders.Add(
                        $"{name} bundles {string.Join("+", bundled)} which needs {package}, "
                        + $"unreachable from what {name} declares under: {string.Join(", ", uncovered)}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A bundled assembly's package dependencies do not flow to consumers — PrivateAssets=\"all\" is what "
            + "keeps the bundled project out of the nuspec — so the host must declare them itself. These do not, "
            + "and a consumer restoring the published package gets a FileNotFoundException at runtime:\n  "
            + string.Join("\n  ", offenders));
    }

    // The unpackable projects whose DLL this host packs into its own lib/ folder. Two mechanisms are in use
    // and both count: TfmSpecificPackageFile with a lib/ PackagePath (Rask.Server, Rask.Wasm) and
    // BuildOutputInPackage. A PrivateAssets="all" ProjectReference on its own does NOT count —
    // the batteries take one to compile against Rask.Core without bundling it, relying on the host package
    // the consumer already has, so demanding they re-declare Core's dependencies would be noise.
    private static IReadOnlyList<string> BundledProjects(XDocument document, Dictionary<string, string> projects) =>
    [
        .. document
            .Descendants()
            .Where(e => e.Name.LocalName is "BuildOutputInPackage"
                || (e.Name.LocalName == "TfmSpecificPackageFile"
                    && e.Attribute("PackagePath")?.Value.Replace('\\', '/')
                        .StartsWith("lib", StringComparison.OrdinalIgnoreCase) == true))
            .Select(e => e.Attribute("Include")?.Value.Replace('\\', '/'))
            .Where(v => v is not null && v.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(v => BundledAssemblyName(v!))
            .Where(projects.ContainsKey)
            .Where(n => !IsPackable(projects[n]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal),
    ];

    // "$(OutputPath)Rask.Core.dll" -> "Rask.Core". The MSBuild property is not a directory, so it carries no
    // separator and Path.GetFileNameWithoutExtension alone hands back "$(OutputPath)Rask.Core" — which matches
    // no project, which silently empties the bundled set and makes this whole guard pass on everything. It did.
    private static string BundledAssemblyName(string include)
    {
        var afterProperty = include.LastIndexOf(')') is var close and >= 0 ? include[(close + 1)..] : include;
        return Path.GetFileNameWithoutExtension(afterProperty);
    }

    private static HashSet<string> PackageReferences(XDocument document) =>
        new(
            document.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => !string.IsNullOrEmpty(v))!,
            StringComparer.OrdinalIgnoreCase);

    // A bundled project can itself bundle another (Rask.Client and Rask.Html both take Rask.Core), and the
    // host packs every one of their DLLs, so the whole chain's package references have to surface.
    private static IEnumerable<string> TransitivePackageReferences(string name, Dictionary<string, string> projects)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([name]);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current) || !projects.TryGetValue(current, out var path))
            {
                continue;
            }

            var document = XDocument.Load(path);
            foreach (var package in PackageReferences(document))
            {
                yield return package;
            }

            foreach (var reference in document.Descendants("ProjectReference"))
            {
                // An analyzer/task reference contributes no runtime assembly, so it drags in no runtime dep.
                if (string.Equals(reference.Attribute("ReferenceOutputAssembly")?.Value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (reference.Attribute("Include")?.Value.Replace('\\', '/') is { } include)
                {
                    pending.Push(Path.GetFileNameWithoutExtension(include));
                }
            }
        }
    }

    // What the host's declared package references actually pull in, per target framework, read from the
    // restore graph rather than guessed. Roots are the host's own PackageReferences — never the bundled
    // ProjectReferences, which is the whole point: their dependencies are exactly what does not flow.
    private static Dictionary<string, HashSet<string>> ReachablePackagesPerTarget(string csprojPath, string name)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(csprojPath)!, "obj", "project.assets.json");

        // Fail rather than skip. Every packable host is in Rask.slnx and the gate builds the whole solution,
        // so a missing restore graph means this ran somewhere the gate never did — and a packaging guard that
        // quietly passes when it cannot see anything is worse than no guard.
        Assert.True(
            File.Exists(assetsPath),
            $"{name} has no restore graph at {assetsPath}, so its transitive packages cannot be resolved. "
            + $"Restore it first: dotnet restore {Path.GetRelativePath(RepoRoot(), csprojPath)}");

        using var stream = File.OpenRead(assetsPath);
        using var assets = JsonDocument.Parse(stream);
        var root = assets.RootElement;

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var framework in root.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (framework.Value.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    roots.Add(dependency.Name);
                }
            }
        }

        var perTarget = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            // "Microsoft.Extensions.Logging/10.0.11" -> its dependency names.
            var graph = target.Value.EnumerateObject().ToDictionary(
                entry => entry.Name.Split('/')[0],
                entry => entry.Value.TryGetProperty("dependencies", out var d)
                    ? d.EnumerateObject().Select(x => x.Name).ToArray()
                    : [],
                StringComparer.OrdinalIgnoreCase);

            var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>(roots);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!reached.Add(current) || !graph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var dependency in next)
                {
                    pending.Push(dependency);
                }
            }

            perTarget[target.Name] = reached;
        }

        return perTarget;
    }

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
