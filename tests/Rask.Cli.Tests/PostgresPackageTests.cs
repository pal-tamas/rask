using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     Rask.Postgres ships stable, at the version of every other package, and stays a package of its own that a
///     PostgreSQL app adds.
/// </summary>
/// <remarks>
///     It was held as <c>x.y.z-rc.1</c> while Npgsql's EF Core 11 provider was a release candidate (#1320). The provider
///     is stable now, so nothing may put a prerelease suffix back on the package or pin a prerelease provider under it.
///     Rask.Server finds it by name (<c>PostgresLoader</c>) instead of referencing it, so an app on SQLite or SQL Server
///     never restores Npgsql.
/// </remarks>
public sealed class PostgresPackageTests
{
    private static string Repo(params string[] parts) => Path.Combine([CliBuildE2E.FindRepoRoot(), .. parts]);

    [Fact]
    public void Rask_Postgres_rides_on_a_stable_Npgsql_provider()
    {
        var provider = RepoPins.Package("Npgsql.EntityFrameworkCore.PostgreSQL");

        var prerelease = provider.Contains('-', StringComparison.Ordinal);

        Assert.False(prerelease,
            $"Npgsql.EntityFrameworkCore.PostgreSQL is pinned at {provider}, a prerelease: a stable Rask.Postgres cannot "
            + "depend on it (NU5104).");
    }

    [Fact]
    public void Rask_Postgres_packs_at_the_version_every_other_package_does()
    {
        var project = XDocument.Load(Repo("src", "Rask.Postgres", "Rask.Postgres.csproj"));

        var overrides = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageVersion" or "Version" or "VersionSuffix" or "MinVerDefaultPreReleaseIdentifiers")
            .Select(e => e.Name.LocalName)
            .ToList();

        Assert.True(overrides.Count == 0,
            $"Rask.Postgres.csproj sets its own version ({string.Join(", ", overrides)}); it packs at MinVer's, like every other package.");
    }

    [Fact]
    public void The_server_package_finds_the_postgres_one_by_name_instead_of_referencing_it()
    {
        var project = XDocument.Load(Repo("src", "Rask.Server", "Rask.Server.csproj"));

        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .Where(include => include.Contains("Rask.Postgres", StringComparison.Ordinal))
            .ToList();

        Assert.True(references.Count == 0,
            "Rask.Server references Rask.Postgres, so every app would restore Npgsql: it stays a separate package that "
            + $"UseRaskDatabase finds by name (App/PostgresLoader.cs). {string.Join(", ", references)}");
    }
}
