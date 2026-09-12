using Microsoft.CodeAnalysis;
using Rask.Data.Generators;

namespace Rask.Data.Generators.Tests;

/// <summary>
/// Drives <see cref="ModelRegistryGenerator"/> over hand-written entities.
/// </summary>
public class ModelRegistryGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ModelRegistryGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void Two_entities_sharing_a_simple_name_in_two_namespaces_still_compile()
    {
        // The regression: the registry named its locals after the simple name, so both declared `__product`.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop.Catalog { public sealed class Product : Model<Guid> { } }
            namespace Shop.Archive { public sealed class Product : Model<Guid> { } }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_Configure_that_is_an_instance_method_is_RASK072()
    {
        var run = Run("""
            using System;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>
            {
                public void Configure(EntityTypeBuilder<Product> builder) { }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK072", diagnostic.Id);
        Assert.Contains("is an instance method", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_strongly_typed_id_with_two_values_is_RASK073()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly record struct PairId(Guid Left, Guid Right);
            public sealed class Pair : Model<PairId> { }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK073", diagnostic.Id);
        Assert.Contains("2 public properties", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
