using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class DatabaseCatalogTests
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    public void Resolves_each_known_database_by_key(string key)
    {
        Assert.True(DatabaseCatalog.TryGet(key, out var database));
        Assert.Equal(key, database.Key);
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        Assert.True(DatabaseCatalog.TryGet("PostgreS", out var database));
        Assert.Equal("postgres", database.Key);
    }

    [Fact]
    public void Unknown_key_falls_back_to_default_and_returns_false()
    {
        Assert.False(DatabaseCatalog.TryGet("mongo", out var database));
        Assert.Equal(DatabaseCatalog.Default, database);
    }

    [Fact]
    public void Sqlite_is_the_default()
    {
        // The One Person Framework thesis is one box, one file. A change here is a change of direction,
        // not a tweak.
        Assert.Equal("sqlite", DatabaseCatalog.Default.Key);
        Assert.True(DatabaseCatalog.Default.IsFileBased);
    }

    [Fact]
    public void Only_sqlite_is_file_based()
    {
        // IsFileBased gates Litestream, snapshots and `rask db backup`. Anything else claiming it would
        // scaffold a backup story that cannot work.
        Assert.Equal(["sqlite"], DatabaseCatalog.All.Where(d => d.IsFileBased).Select(d => d.Key));
    }

    [Fact]
    public void Keys_lists_every_database_for_help_text()
    {
        Assert.Equal(["sqlite", "postgres", "sqlserver"], DatabaseCatalog.Keys);
    }

    [Fact]
    public void Every_entry_is_fully_populated()
    {
        // A blank field here surfaces as a generated file that doesn't compile, far from its cause.
        foreach (var database in DatabaseCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(database.ShortName), database.Key);
            Assert.False(string.IsNullOrWhiteSpace(database.DisplayName), database.Key);
            Assert.False(string.IsNullOrWhiteSpace(database.Package), database.Key);
            Assert.False(string.IsNullOrWhiteSpace(database.Namespace), database.Key);
            Assert.False(string.IsNullOrWhiteSpace(database.UseMethod), database.Key);
            Assert.False(string.IsNullOrWhiteSpace(database.DefaultConnectionString), database.Key);
            Assert.False(string.IsNullOrWhiteSpace(database.EfPackage), database.Key);
            Assert.False(string.IsNullOrWhiteSpace(database.TestUseMethod), database.Key);
        }
    }

    [Fact]
    public void For_maps_every_enum_value_to_an_entry()
    {
        foreach (var provider in Enum.GetValues<DatabaseProvider>())
        {
            Assert.Equal(provider, DatabaseCatalog.For(provider).Provider);
        }
    }

    [Fact]
    public void DetectProvider_reads_postgres_off_a_package_reference()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Rask.Postgres" Version="1.0.0"/>
              </ItemGroup>
            </Project>
            """;

        Assert.Equal(DatabaseProvider.Postgres, DatabaseCatalog.DetectProvider(csproj));
    }

    [Fact]
    public void DetectProvider_falls_back_to_sqlite()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Rask.SQLite.EntityFrameworkCore" Version="1.0.0"/>
              </ItemGroup>
            </Project>
            """;

        Assert.Equal(DatabaseProvider.Sqlite, DatabaseCatalog.DetectProvider(csproj));
    }

    [Fact]
    public void DetectProvider_defaults_to_sqlite_when_no_database_package_is_referenced()
    {
        // A project with no database yet is about to have one scaffolded into it, and that one is SQLite.
        Assert.Equal(DatabaseProvider.Sqlite, DatabaseCatalog.DetectProvider("<Project/>"));
    }

    [Fact]
    public void DetectProvider_ignores_a_package_named_only_in_a_comment()
    {
        // Matching a bare substring would let prose decide the provider, which then silently mis-scaffolds.
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <!-- Considered Rask.Postgres, stayed on SQLite for now. -->
              <ItemGroup>
                <PackageReference Include="Rask.SQLite.EntityFrameworkCore" Version="1.0.0"/>
              </ItemGroup>
            </Project>
            """;

        Assert.Equal(DatabaseProvider.Sqlite, DatabaseCatalog.DetectProvider(csproj));
    }

    [Fact]
    public void DetectProvider_prefers_the_specific_provider_when_both_are_referenced()
    {
        // SQLite is the fallback, so an app carrying both references is the more specific one — treating it
        // as SQLite would be the silent-wrong-answer case.
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Rask.SQLite.EntityFrameworkCore" Version="1.0.0"/>
                <PackageReference Include="Rask.Postgres" Version="1.0.0"/>
              </ItemGroup>
            </Project>
            """;

        Assert.Equal(DatabaseProvider.Postgres, DatabaseCatalog.DetectProvider(csproj));
    }
}
