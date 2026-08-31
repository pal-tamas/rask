using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     Tailwind is built into the host packages, and there is no way to turn it off.
/// </summary>
/// <remarks>
///     <para>
///         Structural, like <see cref="PackagingContractTests" /> and for the same reason: every defect
///         these guard against lives on the far side of a <c>dotnet pack</c>, and is invisible from
///         inside this repository. In-repo projects get the Tailwind build from
///         <c>Directory.Build.{props,targets}</c>, so every sample compiles a stylesheet whether or not
///         the packed route works at all — a consumer is the only one who finds out, and what they find
///         is a page of unstyled HTML rather than an error.
///     </para>
///     <para>
///         The build-time behaviour these describe is exercised for real by
///         <c>ProjectGeneratorBuildE2ETests</c>, which compiles a scaffolded app against a packed feed
///         and reads the stylesheet back.
///     </para>
/// </remarks>
public sealed class TailwindBuiltInTests
{
    private static readonly string _repoRoot = CliBuildE2E.FindRepoRoot();

    /// <summary>The packages an app references to get a host — and, with it, Tailwind.</summary>
    public static TheoryData<string> HostPackages() => new() { "Rask.Server", "Rask.Wasm" };

    /// <summary>
    ///     Tailwind reaches a consumer through the host package, not through a package they add.
    /// </summary>
    /// <remarks>
    ///     NuGet auto-imports only <c>build/&lt;PackageId&gt;.{props,targets}</c>, so the payload
    ///     <c>RaskTailwindBuildPack</c> packs beside those files is dead bytes unless they import it.
    ///     Both halves matter and fail differently: without the props every <c>RaskTailwind*</c> default
    ///     is empty (no version to download, no input path to look for), and without the targets nothing
    ///     compiles at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(HostPackages))]
    public void The_entry_points_import_the_shared_tailwind_build_integration(string package)
    {
        foreach (var extension in new[] { "props", "targets" })
        {
            var entry = Path.Combine(_repoRoot, "src", package, "build", $"{package}.{extension}");

            Assert.True(
                File.Exists(entry),
                $"{package} has no build/{package}.{extension}, so NuGet will never import the Tailwind "
                + $"{extension} and a scaffolded app gets no stylesheet.");

            var import = XDocument.Load(entry).Descendants("Import").SingleOrDefault(e =>
                (e.Attribute("Project")?.Value ?? string.Empty)
                    .EndsWith($"Rask.Tailwind.{extension}", StringComparison.Ordinal));

            Assert.True(
                import is not null,
                $"build/{package}.{extension} does not import Rask.Tailwind.{extension}, so what "
                + "RaskTailwindBuildPack packs beside it is never read.");

            // The guard is load-bearing, not cosmetic: there is no Rask.Tailwind.{extension} sibling in
            // the source tree (it is packed from Rask.Tailwind's own build/ folder), and Rask.Wasm's
            // build/ folder is imported in-repo by Directory.Build.targets — so an unguarded import of a
            // missing file is MSB4019 across every project in the repository.
            Assert.Contains(
                "Exists(",
                import!.Attribute("Condition")?.Value ?? string.Empty,
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(HostPackages))]
    public void Each_host_package_packs_the_shared_tailwind_build_integration(string package)
    {
        var csproj = XDocument.Load(Path.Combine(_repoRoot, "src", package, $"{package}.csproj"));

        Assert.Contains(
            csproj.Descendants("Import"),
            e => (e.Attribute("Project")?.Value ?? string.Empty)
                .EndsWith("RaskTailwindBuildPack.targets", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Rask.Tailwind is not published as a package of its own.
    /// </summary>
    /// <remarks>
    ///     Not tidiness, and not merely redundant. An app that referenced both a host package and
    ///     Rask.Tailwind would import the same targets twice, and the compile target would run the
    ///     Tailwind compiler twice over one output file. Rask.Core and Rask.Html are
    ///     <c>IsPackable=false</c> for the same reason.
    /// </remarks>
    [Fact]
    public void Rask_Tailwind_is_payload_rather_than_a_package_of_its_own()
    {
        var csproj = XDocument.Load(
            Path.Combine(_repoRoot, "src", "Rask.Tailwind", "Rask.Tailwind.csproj"));

        // Read as XML rather than as text: the file's own comments talk about PackageId, and a
        // substring check over prose is how a guard passes on a project it never actually inspected.
        Assert.Contains(
            csproj.Descendants("IsPackable"),
            e => e.Value.Equals("false", StringComparison.Ordinal));

        Assert.Empty(csproj.Descendants("PackageId"));
    }

    /// <summary>
    ///     A scaffolded project references no Tailwind package, because there is none to reference.
    /// </summary>
    /// <remarks>
    ///     The other half of the contract above. Asserted here rather than only in the generator tests
    ///     because the two have to move together: the day the reference comes back is the day every
    ///     scaffolded app compiles its stylesheet twice.
    /// </remarks>
    [Fact]
    public void The_scaffolder_emits_no_tailwind_package_reference()
    {
        var scaffolding = Path.Combine(_repoRoot, "src", "Rask.Cli", "Scaffolding");

        foreach (var file in Directory.EnumerateFiles(scaffolding, "*.cs"))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                // Prose about how the build works is fine; a PackageReference is not.
                Assert.False(
                    line.Contains("PackageReference", StringComparison.Ordinal)
                    && line.Contains("Rask.Tailwind", StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} emits a Rask.Tailwind PackageReference. Tailwind ships "
                    + "inside the host package now, so a scaffolded csproj naming it imports the same "
                    + "targets a second time and compiles the stylesheet twice.");
            }
        }
    }

    /// <summary>
    ///     There is no property that turns the Tailwind build off.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Styling is not a feature of a Rask app, it is how one is built: every scaffolded project is
    ///         written in utilities, so a build that quietly produced no CSS would render every page as
    ///         unstyled HTML — a failure nobody notices until it is in front of a user, and one that no
    ///         test of the C# can see.
    ///     </para>
    ///     <para>
    ///         Asserted on the gate rather than on the absence of one property name, because a switch can
    ///         come back under any spelling. What the gate is allowed to depend on is exactly two things:
    ///         whether this is a design-time build (an IDE reloading a project must never download a
    ///         binary or shell out to a compiler) and whether the project has a stylesheet to compile at
    ///         all. The second is what makes the file safe to import into every project in a solution,
    ///         and it is not an off switch: a project that HAS a stylesheet cannot decline to compile it.
    ///     </para>
    /// </remarks>
    [Fact]
    public void The_tailwind_build_has_no_off_switch()
    {
        var buildDir = Path.Combine(_repoRoot, "src", "Rask.Tailwind", "build");

        var gate = XDocument.Load(Path.Combine(buildDir, "Rask.Tailwind.targets"))
            .Descendants()
            .Single(e => e.Name.LocalName == "_RaskTailwindDoBuild")
            .Attribute("Condition")!.Value;

        Assert.Contains("DesignTimeBuild", gate, StringComparison.Ordinal);
        Assert.Contains("Exists('$(_RaskTailwindInput)')", gate, StringComparison.Ordinal);

        // Nothing else: any third term is another way to build a Rask app with no stylesheet.
        Assert.Equal(2, gate.Split(" AND ", StringSplitOptions.RemoveEmptyEntries).Length);

        // And no default is declared for one, so it cannot be reintroduced from the command line either.
        Assert.DoesNotContain(
            "RaskTailwindBuild",
            File.ReadAllText(Path.Combine(buildDir, "Rask.Tailwind.props")),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     No error message offers an off switch as the way out.
    /// </summary>
    /// <remarks>
    ///     Every failure in the resolver used to end with "or set RaskTailwindBuild=false". Leaving one
    ///     behind would be worse than the switch itself: an error naming a property that no longer does
    ///     anything sends someone to a dead end while their build stays red.
    /// </remarks>
    [Fact]
    public void No_error_message_points_at_a_switch_that_no_longer_exists()
    {
        var resolver = Path.Combine(
            _repoRoot, "src", "Rask.Tailwind.Tasks", "ResolveTailwindCliTask.cs");

        Assert.DoesNotContain(
            "RaskTailwindBuild", File.ReadAllText(resolver), StringComparison.Ordinal);
    }
}
