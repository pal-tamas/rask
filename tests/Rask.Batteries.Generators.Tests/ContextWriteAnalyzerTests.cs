using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK133: app code that writes an aggregate on a DbContext and saves it there.</summary>
public class ContextWriteAnalyzerTests
{
    // A factory with its generated entry written by hand, a plain entity beside the aggregate, and the app's context.
    private static string Source(string work, string method = "Run", string attributes = "") => $$"""
        using System;
        using System.CodeDom.Compiler;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        namespace Shop;
        public sealed class FactAttribute : Attribute;
        public sealed partial class Product : Aggregate<Guid, Versioned>
        {
            private Product() { }
            public string Name { get; private set; } = "";
            internal static Product Place(string name) => new() { Name = name };

            [GeneratedCode("Rask.Operations", "1")]
            public static Task<Guid> Place(string name, CancellationToken cancellationToken = default) => Task.FromResult(Guid.Empty);
        }
        public sealed class Note : Entity<Guid>;
        public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
        {
            public DbSet<Product> Products => Set<Product>();
            public DbSet<Note> Notes => Set<Note>();
        }
        public static class Page
        {
            {{attributes}}
            public static async Task {{method}}(ShopDb db, Product product, Note note, string name, DbContextOptions<ShopDb> options, CancellationToken token)
            {
                {{work}}
            }
        }
        """;

    [Theory]
    [InlineData("db.Products.Add(product); await db.SaveChangesAsync();", "Add")]
    [InlineData("db.Add(product); db.SaveChanges();", "Add")]
    [InlineData("await db.Products.AddAsync(product); await db.SaveChangesAsync(token);", "AddAsync")]
    [InlineData("db.AddRange(product, product); await db.SaveChangesAsync();", "AddRange")]
    [InlineData("db.Products.AddRange([product]); await db.SaveChangesAsync();", "AddRange")]
    [InlineData("db.Update(product); await db.SaveChangesAsync();", "Update")]
    [InlineData("db.Products.Remove(product); await db.SaveChangesAsync();", "Remove")]
    [InlineData("db.Remove((object)product); await db.SaveChangesAsync();", "Remove")]
    [InlineData("db.Products.RemoveRange(product); await db.SaveChangesAsync();", "RemoveRange")]
    public async Task A_write_of_an_aggregate_a_context_then_saves_is_RASK133(string work, string write)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ContextWriteAnalyzer(), source);

        var flagged = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Info, flagged.Severity);
        Assert.StartsWith("'" + write + "' writes the aggregate 'Product'", flagged.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("db.Products.Add(product);")]
    [InlineData("db.Notes.Add(note); await db.SaveChangesAsync();")]
    [InlineData("db.Add(note); await db.SaveChangesAsync();")]
    [InlineData("_ = db.Products.Find(product.Id); await db.SaveChangesAsync();")]
    public async Task A_write_nothing_saves_or_a_write_of_a_plain_entity_is_not_RASK133(string work)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ContextWriteAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("SeedAsync", "")]
    [InlineData("Run", "[Fact]")]
    public async Task A_seed_and_a_test_write_their_fixtures_on_the_context(string method, string attributes)
    {
        var source = Source("db.Products.Add(product); await db.SaveChangesAsync();", method, attributes);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ContextWriteAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_work_of_UseSeeding_is_a_seed()
    {
        var source = Source("_ = new DbContextOptionsBuilder().UseSeeding((c, _) => { c.Add(product); c.SaveChanges(); });");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ContextWriteAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_file_under_Migrations_is_not_RASK133()
    {
        var source = Source("db.Products.Add(product); await db.SaveChangesAsync();");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ContextWriteAnalyzer(), source, path: "Migrations/Seed.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_factory_added_to_a_context_of_its_own_and_saved_at_once_offers_the_factory_entry()
    {
        var source = Source("await using var shop = new ShopDb(options); shop.Add(Product.Place(name)); await shop.SaveChangesAsync(token);");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ContextWriteAnalyzer(), source);

        var flagged = Assert.Single(diagnostics);
        Assert.Equal("Place", flagged.Properties[ContextWriteAnalyzer.FactoryProperty]);
        Assert.True(flagged.Properties.ContainsKey(ContextWriteAnalyzer.TokenProperty));
        Assert.Contains("'Product.Place(…)'", flagged.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("db.Add(Product.Place(name)); await db.SaveChangesAsync();")]
    [InlineData("await using var shop = new ShopDb(options); shop.Add(note); shop.Add(Product.Place(name)); await shop.SaveChangesAsync();")]
    [InlineData("await using var shop = new ShopDb(options); shop.Add(Product.Place(name)); shop.SaveChanges();")]
    public async Task A_context_that_may_hold_other_writes_offers_no_mechanical_rewrite(string work)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ContextWriteAnalyzer(), source);

        Assert.All(diagnostics, d => Assert.False(d.Properties.ContainsKey(ContextWriteAnalyzer.FactoryProperty)));
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_a_write_around_the_aggregate()
    {
        var strict = StrictConfig.Severity("RASK133");
        var source = Source("db.Products.Add(product); await db.SaveChangesAsync();");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ContextWriteAnalyzer(), source, options => options.WithSpecificDiagnosticOptions([new("RASK133", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
    }
}
