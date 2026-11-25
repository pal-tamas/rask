using System.Text.Json;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     A scaffold reached through a symbolic link restores its test project WITH the reference to the app.
/// </summary>
/// <remarks>
///     <para>
///         macOS's temp directory is under <c>/var</c>, a link to <c>/private/var</c>. NuGet's default restore walks
///         the references by running a target in each one, and the referenced app reported itself under the resolved
///         path while the test project had recorded the link's — two strings for one file — so restore dropped the
///         reference and <c>Shop.Tests</c> lost every package the app brings. The scaffold's Directory.Build.props
///         turns on the static-graph restore, which keeps the paths as written.
///     </para>
///     <para>
///         Restored for real by <c>dotnet restore</c>, against an empty package source: the packages fail to resolve,
///         but the assets file still records the project graph, which is the whole claim.
///     </para>
/// </remarks>
public sealed class LinkedScaffoldRestoreTests
{
    [Fact]
    public async Task Under_a_symlinked_folder_the_test_project_restores_with_its_reference_to_the_app()
    {
        var real = Directory.CreateTempSubdirectory("rask-linked-scaffold-");
        var link = Path.Combine(Path.GetTempPath(), "rask-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(link, real.FullName);
        var app = Path.Combine(link, "Shop");
        var emptySource = Directory.CreateDirectory(Path.Combine(link, "empty-source")).FullName;
        _ = TemplateCatalog.TryGet("server", out var template);
        Write(ProjectGenerator.GenerateServer(app, "Shop", BatterySelection.ToBatteries(template, []), "9.9.9"));
        var testsCsproj = Path.Combine(app, "Shop.Tests", "Shop.Tests.csproj");

        try
        {
            var result = await TestProcess.Run(
                "dotnet", ["restore", testsCsproj, "--source", emptySource, "-nologo"], app,
                cancellationToken: TestContext.Current.CancellationToken);

            var references = ProjectReferences(Path.Combine(app, "Shop.Tests", "obj", "project.assets.json"));
            Assert.True(
                references.Any(reference => Path.GetFileName(reference) == "Shop.csproj" && File.Exists(reference)),
                $"Shop.Tests restored without its reference to Shop ({string.Join(", ", references)}):{Environment.NewLine}{result.Output}");
        }
        finally
        {
            Directory.Delete(link);
            real.Delete(recursive: true);
        }
    }

    private static void Write(ScaffoldResult scaffold)
    {
        foreach (var file in scaffold.Files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            File.WriteAllText(file.Path, file.Content);
        }
    }

    private static List<string> ProjectReferences(string assetsFile)
    {
        Assert.True(File.Exists(assetsFile), $"dotnet restore wrote no {assetsFile}.");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));

        return
        [
            .. assets.RootElement.GetProperty("project").GetProperty("restore").GetProperty("frameworks")
                .EnumerateObject()
                .SelectMany(framework => framework.Value.GetProperty("projectReferences").EnumerateObject())
                .Select(reference => reference.Name),
        ];
    }
}
