using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

// #1083: a scaffold pinned Rask.Storage and Rask.DevTools at a version nuget.org never had, and the restore said only
// NU1103. The feed check names them first; it never stops the command, and a feed it cannot reach reports nothing.
public sealed class PackageFeedTests
{
    [Fact]
    public void The_Rask_references_a_project_pins_are_read_with_their_versions()
    {
        var files = new[]
        {
            new ScaffoldFile("/proj/Shop/Shop.csproj", """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <ItemGroup>
                    <PackageReference Include="Rask.Server" Version="0.21.0"/>
                    <PackageReference Include="Rask.Storage" Version="0.21.0" />
                    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0"/>
                  </ItemGroup>
                </Project>
                """),
            new ScaffoldFile("/proj/Shop/Program.cs", """<PackageReference Include="Rask.NotAProject" Version="9.9.9"/>"""),
        };

        var references = PackageFeed.RaskReferences(files);

        Assert.Equal([("Rask.Server", "0.21.0"), ("Rask.Storage", "0.21.0")], references);
    }

    [Fact]
    public async Task A_pinned_version_the_feed_lacks_is_reported_and_one_it_has_is_not()
    {
        var feed = Feed(new()
        {
            ["Rask.Server"] = ["0.20.0", "0.21.0"],
            ["Rask.Storage"] = ["0.21.1-alpha.0.27"],
        });

        var unpublished = await feed.FindUnpublishedAsync(
            [("Rask.Server", "0.21.0"), ("Rask.Storage", "0.21.0")], CancellationToken.None);

        Assert.Equal([("Rask.Storage", "0.21.0")], unpublished);
    }

    [Fact]
    public async Task A_package_the_feed_has_never_heard_of_is_reported()
    {
        var feed = Feed(new() { ["Rask.DevTools"] = [] });

        var unpublished = await feed.FindUnpublishedAsync([("Rask.DevTools", "0.21.0")], CancellationToken.None);

        Assert.Equal([("Rask.DevTools", "0.21.0")], unpublished);
    }

    [Fact]
    public async Task A_feed_that_cannot_be_asked_reports_nothing()
    {
        var feed = new PackageFeed((_, _) => Task.FromResult<IReadOnlyCollection<string>?>(null));

        var unpublished = await feed.FindUnpublishedAsync([("Rask.Storage", "0.21.0")], CancellationToken.None);

        Assert.Empty(unpublished);
    }

    private static PackageFeed Feed(Dictionary<string, string[]> versions) =>
        new((id, _) => Task.FromResult<IReadOnlyCollection<string>?>(versions.TryGetValue(id, out var v) ? v : null));
}
