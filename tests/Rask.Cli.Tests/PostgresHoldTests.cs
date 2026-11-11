using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     Rask.Postgres is held as a release candidate, and unbundled from Rask.Server, exactly while the Npgsql EF Core
///     provider it rides on is a prerelease.
/// </summary>
/// <remarks>
///     A stable package cannot depend on a prerelease one (NU5104), so while Npgsql's provider is a release candidate
///     Rask.Postgres packs as <c>x.y.z-rc.N</c> and no other package references it. When Dependabot moves the provider to
///     a stable release, the hold has to go with it — the suffix, Rask.Server's by-name <c>PostgresLoader</c> and the
///     NU1608 NoWarns — and this is what fails until it has.
/// </remarks>
public sealed class PostgresHoldTests
{
    private static string Repo(params string[] parts) => Path.Combine([CliBuildE2E.FindRepoRoot(), .. parts]);

    private static bool HeldAsPreRelease() =>
        XDocument.Load(Repo("src", "Rask.Postgres", "Rask.Postgres.csproj"))
            .Descendants("RaskPostgresPreRelease")
            .Any(e => e.Value.Trim().Length > 0);

    [Fact]
    public void Rask_Postgres_is_held_as_a_prerelease_exactly_while_its_Npgsql_provider_is_one()
    {
        var provider = RepoPins.Package("Npgsql.EntityFrameworkCore.PostgreSQL");

        var held = HeldAsPreRelease();

        Assert.True(
            held == provider.Contains('-', StringComparison.Ordinal),
            held
                ? $"Npgsql.EntityFrameworkCore.PostgreSQL is pinned at {provider}, a stable release, but Rask.Postgres is still "
                  + "held as a prerelease. Lift the hold: drop RaskPostgresPreRelease and the NU1608 NoWarns, and make "
                  + "Rask.Server reference Rask.Postgres again in place of PostgresLoader."
                : $"Npgsql.EntityFrameworkCore.PostgreSQL is pinned at {provider}, a prerelease, so Rask.Postgres must pack "
                  + "as one too (RaskPostgresPreRelease), or every stable package that depends on it fails NU5104.");
    }

    [Fact]
    public void No_shipped_project_references_Rask_Postgres_while_it_is_held()
    {
        var held = HeldAsPreRelease();

        var referrers = Directory.EnumerateFiles(Repo("src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Rask.Postgres{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => XDocument.Load(path).Descendants("ProjectReference")
                .Any(r => ((string?)r.Attribute("Include") ?? "").EndsWith("Rask.Postgres.csproj", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(!held || referrers.Count == 0,
            $"Rask.Postgres is held as a prerelease, so a stable package referencing it fails NU5104: {string.Join(", ", referrers)}.");
    }
}
