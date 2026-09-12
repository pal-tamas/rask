using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     What <c>rask new --framework</c> writes: every project and container names the version that was asked
///     for, and the default writes the committed trees unchanged.
/// </summary>
/// <remarks>
///     <para>
///         Rask itself ships for both .NET versions, so the choice decides only what the scaffolded app
///         targets. .NET 10 stays the default because it is the LTS release and an app inherits its support
///         window from the runtime it names.
///     </para>
///     <para>
///         The half that matters is the DEFAULT staying byte-for-byte identical: the templates are committed
///         trees, and a rewrite that fired when nobody asked for one would edit files nobody reviewed.
///     </para>
/// </remarks>
public sealed class ScaffoldFrameworkTests
{
    private const string Root = "/scaffold-root";
    private const string Version = "1.0.0";

    public static TheoryData<string> SpaKeys
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var framework in SpaFramework.All)
            {
                data.Add(framework.Key);
            }

            return data;
        }
    }

    [Fact]
    public void The_default_scaffold_is_the_committed_tree_unchanged()
    {
        var implicitly = ProjectGenerator.GenerateServer(Root, "Shop", new ServerBatteries(), Version);
        var explicitly = ProjectGenerator.GenerateServer(
            Root, "Shop", new ServerBatteries(), Version, dotnet: DotnetTarget.Default);

        Assert.Equal(
            implicitly.Files.Select(f => (f.Path, f.Content)),
            explicitly.Files.Select(f => (f.Path, f.Content)));
        Assert.Contains(
            implicitly.Files,
            f => f.Path.EndsWith(".csproj", StringComparison.Ordinal)
                && f.Content.Contains("<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal));

        // And the default rewrite is the identity, checked by reference. The two calls above take the
        // SAME branch, so on their own they would agree just as happily about corrupted text.
        var csproj = Single(implicitly, ".csproj");
        Assert.Same(csproj, DotnetTarget.Default.Rewrite(csproj));
    }

    [Fact]
    public void A_server_app_on_the_second_version_names_it_everywhere()
    {
        var result = ProjectGenerator.GenerateServer(
            Root, "Shop", new ServerBatteries { Docker = true }, Version, dotnet: DotnetTarget.Preview);

        var csproj = Single(result, ".csproj");
        Assert.Contains("<TargetFramework>net11.0</TargetFramework>", csproj, StringComparison.Ordinal);

        // The container builds and runs the same app: a csproj on one version and an image on another is a
        // restore error at `docker build` time that names neither.
        var dockerfile = Single(result, "Dockerfile");
        Assert.Contains("dotnet/sdk:11.0", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet/aspnet:11.0", dockerfile, StringComparison.Ordinal);

        Assert.DoesNotContain(result.Files.Where(f => f.Bytes is null), f => NamesTheDefault(f.Content));
    }

    [Fact]
    public void A_browser_app_takes_the_matching_browser_framework()
    {
        // The wasm template targets net10.0-browser, so its rewrite is the -browser spelling rather than the
        // plain one — the case a single literal replacement would have missed.
        var result = ProjectGenerator.GenerateWasm(
            Root, "Shop", pwa: false, docker: false, Version, dotnet: DotnetTarget.Preview);

        Assert.Contains(
            "<TargetFramework>net11.0-browser</TargetFramework>", Single(result, ".csproj"), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SpaKeys))]
    public void Every_front_end_template_carries_the_choice_into_its_host(string key)
    {
        Assert.True(SpaFramework.TryGet(key, out var framework));

        var result = ProjectGenerator.GenerateSpa(
            Root, "Shop", framework, new ServerBatteries(), Version, DotnetTarget.Preview);

        Assert.DoesNotContain(result.Files.Where(f => f.Bytes is null), f => NamesTheDefault(f.Content));

        // And it NAMES the version asked for. Absence on its own would be satisfied by a rewrite that
        // deleted the element outright, which is a project that no longer builds at all.
        Assert.Contains(
            "<TargetFramework>net11.0</TargetFramework>", Single(result, ".csproj"), StringComparison.Ordinal);
    }

    public static TheoryData<string> MetaKeys
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var template in MetaTemplate.All)
            {
                data.Add(template.Key);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MetaKeys))]
    public void Every_meta_template_carries_the_choice_into_its_host(string key)
    {
        Assert.True(MetaTemplate.TryGet(key, out var meta));

        var result = ProjectGenerator.GenerateMeta(
            Root, "Shop", meta, new ServerBatteries { Docker = true }, Version, DotnetTarget.Preview);

        Assert.DoesNotContain(result.Files.Where(f => f.Bytes is null), f => NamesTheDefault(f.Content));
        Assert.Contains(
            "<TargetFramework>net11.0</TargetFramework>", Single(result, ".csproj"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_editor_launch_configuration_points_at_build_output_that_will_exist()
    {
        // .vscode/launch.json names the dll by its BUILD PATH, and the .vscode tree used to be assembled
        // after the rewrite had already run — so F5 on a net11.0 scaffold started bin/Debug/net10.0/Shop.dll,
        // which that build never produces. It is neither a csproj nor a Dockerfile, so the verify did not
        // look at it either: the project compiled, and only pressing F5 showed anything.
        var result = ProjectGenerator.GenerateServer(
            Root, "Shop", new ServerBatteries(), Version, dotnet: DotnetTarget.Preview);

        var launch = Single(result, "launch.json");
        Assert.Contains("bin/Debug/net11.0/", launch, StringComparison.Ordinal);
        Assert.DoesNotContain("bin/Debug/net10.0/", launch, StringComparison.Ordinal);
    }

    [Fact]
    public void An_island_project_takes_the_choice_into_its_component_library()
    {
        // The Blazor island adds a component library with a framework of its own, and it is assembled
        // after the tree is materialised. Rewriting only what the materialising loop read left that csproj
        // on net10.0 — which the verify then caught, turning a legitimate flag combination into an
        // unhandled exception in the user's face rather than a scaffold.
        var result = ProjectGenerator.GenerateServer(
            Root, "Shop", new ServerBatteries(), Version, islands: ["blazor"], dotnet: DotnetTarget.Preview);

        var library = result.Files
            .Where(f => f.Bytes is null)
            .Single(f => f.Path.EndsWith("Components.csproj", StringComparison.Ordinal));

        Assert.Contains("<TargetFramework>net11.0</TargetFramework>", library.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Files.Where(f => f.Bytes is null), f => NamesTheDefault(f.Content));
    }

    [Theory]
    [InlineData("<TargetFramework>net10.0</TargetFramework>")]
    [InlineData("<TargetFramework>net10.0-browser</TargetFramework>")]
    [InlineData("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build")]
    [InlineData("FROM mcr.microsoft.com/dotnet/aspnet:10.0")]
    [InlineData("\"program\": \"${workspaceFolder}/bin/Debug/net10.0/Shop.dll\"")]
    public void Every_spelling_the_rewrite_changes_is_one_the_check_can_see(string text)
    {
        // The verify is only ever as good as this predicate. A literal the rewrite changes but the check
        // cannot see is a silent miss BY CONSTRUCTION — which is precisely how launch.json got through —
        // so the two are pinned together here rather than trusted to stay in step.
        Assert.NotEqual(text, DotnetTarget.Preview.Rewrite(text));
        Assert.True(DotnetTarget.Preview.StillNamesTheDefault(text));
    }

    private static string Single(ScaffoldResult result, string suffix) =>
        result.Files
            .Where(f => f.Bytes is null)
            .Where(f => f.Path.EndsWith(suffix, StringComparison.Ordinal))
            .Select(f => f.Content)
            .First();

    // Only the spellings the rewrite owns. A comment or a doc line that happens to say "net10.0" is not a
    // framework the project builds for, and failing on one would make this test about prose.
    private static bool NamesTheDefault(string content) =>
        content.Contains("<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal)
        || content.Contains("<TargetFramework>net10.0-browser</TargetFramework>", StringComparison.Ordinal)
        || content.Contains("dotnet/sdk:10.0", StringComparison.Ordinal)
        || content.Contains("dotnet/aspnet:10.0", StringComparison.Ordinal);
}
