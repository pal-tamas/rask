using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class CacheGeneratorTests
{
    [Fact]
    public void Generates_under_features_shared_with_folder_namespace()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var result = CacheGenerator.Generate(project, "/proj", "PopularProducts", feature: null, outputOverride: null);

        var file = Assert.Single(result.Files);
        Assert.Equal(Path.GetFullPath("/proj/Features/Shared/PopularProducts.cs"), Path.GetFullPath(file.Path));
        Assert.Contains("namespace MyApp.Features.Shared;", file.Content, StringComparison.Ordinal);
        Assert.EndsWith("\n", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Co_locates_into_a_feature_slice_when_one_is_named()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var result = CacheGenerator.Generate(project, "/proj", "PopularProducts", feature: "Catalog", outputOverride: null);

        var file = Assert.Single(result.Files);
        Assert.Equal(Path.GetFullPath("/proj/Features/Catalog/PopularProducts.cs"), Path.GetFullPath(file.Path));
        Assert.Contains("namespace MyApp.Features.Catalog;", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_read_through_accessor_that_owns_its_key_and_invalidation()
    {
        // The whole point of the generated type: one place owns the key, and one owns invalidation.
        // Scattered inline string keys are how a cache ends up with a stale entry nobody can find.
        var source = CacheGenerator.Render("MyApp.Features.Catalog", "PopularProducts");

        Assert.Contains("public sealed class PopularProducts(ICache cache)", source, StringComparison.Ordinal);
        Assert.Contains("""private const string Key = "popularproducts:v1";""", source, StringComparison.Ordinal);
        Assert.Contains("cache.GetOrCreateAsync(", source, StringComparison.Ordinal);
        Assert.Contains("public Task InvalidateAsync(", source, StringComparison.Ordinal);
        Assert.Contains("cache.RemoveAsync(Key, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Requests_the_cache_package()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var result = CacheGenerator.Generate(project, "/proj", "Report", feature: null, outputOverride: null);

        Assert.Equal(["Rask.Cache"], result.Packages);
    }

    [Fact]
    public void Notes_cover_registration_schema_and_invalidation()
    {
        // A cache that is never invalidated is the failure mode worth naming in the next steps.
        var notes = CacheGenerator.Notes("PopularProducts");

        Assert.Contains("AddRaskCache<AppDbContext>();", notes, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.AddRaskCache();", notes, StringComparison.Ordinal);
        Assert.Contains("rask db add AddCache", notes, StringComparison.Ordinal);
        Assert.Contains("InvalidateAsync", notes, StringComparison.Ordinal);
    }
}
