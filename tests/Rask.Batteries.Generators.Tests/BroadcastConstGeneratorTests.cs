namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     The <c>Broadcast</c> trait, which is what makes a save announce itself to pages that did not make it.
///     Without this the base compiles, the build is green, and <c>Live()</c> silently never fires.
/// </summary>
public class BroadcastConstGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ModelRegistryGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void An_aggregate_that_names_Broadcast_is_registered_with_it()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable, Broadcast>
            {
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var conventions = run.GeneratedSource("__RaskConventions");
        Assert.Contains(
            "Declare(typeof(global::Shop.Order), (global::Rask.Data.TraitSet)319)",
            conventions,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_that_does_not_name_it_is_registered_without_it()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, SoftDelete>
            {
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var conventions = run.GeneratedSource("__RaskConventions");
        Assert.Contains("Declare(typeof(global::Shop.Order), (global::Rask.Data.TraitSet)95)", conventions, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_that_names_nothing_registers_nothing()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskConventions"));
    }
}
