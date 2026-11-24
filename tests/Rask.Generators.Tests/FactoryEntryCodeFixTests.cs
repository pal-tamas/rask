using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK133 quick-fix: a factory added to a context of its own and saved becomes the factory's entry.</summary>
public class FactoryEntryCodeFixTests
{
    private static string Source(string work) => $$"""
        using System;
        using System.CodeDom.Compiler;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        namespace Shop;
        public sealed partial class Product : Aggregate<Guid>
        {
            private Product() { }
            public string Name { get; private set; } = "";
            internal static Product Place(string name) => new() { Name = name };

            [GeneratedCode("Rask.Operations", "1")]
            public static Task<Guid> Place(string name, CancellationToken cancellationToken = default) => Task.FromResult(Guid.Empty);
        }
        public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options);
        public static class Page
        {
            public static async Task Run(string name, DbContextOptions<ShopDb> options, CancellationToken token)
            {
                {{work}}
            }
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new ContextWriteAnalyzer(), new FactoryEntryCodeFixProvider(), "RASK133", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire",
            "Microsoft.EntityFrameworkCore");

    [Theory]
    [InlineData("await using var db = new ShopDb(options);\n        db.Add(Product.Place(name));\n        await db.SaveChangesAsync(token);",
        "await Product.Place(name, cancellationToken: token);")]
    [InlineData("await using var db = new ShopDb(options);\n        db.Add(Product.Place(name.Trim()));\n        await db.SaveChangesAsync();",
        "await Product.Place(name.Trim(), cancellationToken: default);")]
    public async Task The_RASK133_fix_calls_the_factorys_entry_in_place_of_the_context(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("new ShopDb(options)", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_context_the_method_uses_for_more_is_left_for_a_person_to_rewrite()
    {
        var source = Source("using var db = new ShopDb(options);\n        db.Set<Product>();\n        db.Add(Product.Place(name));\n        await db.SaveChangesAsync();");

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new ContextWriteAnalyzer(), new FactoryEntryCodeFixProvider(), "RASK133", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire",
            "Microsoft.EntityFrameworkCore");

        Assert.False(offered);
    }
}
