namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     The <c>Broadcast</c> const, which is what makes a save announce itself to pages that did not make it.
///     Without this the const compiles, the build is green, and <c>Live()</c> silently never fires.
/// </summary>
public class BroadcastConstGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ModelRegistryGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void An_entity_that_declares_Broadcast_is_registered()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                public const Broadcasts Broadcast = Broadcasts.OnCommit;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var conventions = run.GeneratedSource("__RaskConventions");
        Assert.Contains(
            "Declare(typeof(global::Shop.Order), (global::Rask.Data.Broadcasts)1)",
            conventions,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_that_says_nothing_declares_no_broadcast()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                public const Deletion Deletes = Deletion.Soft;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var conventions = run.GeneratedSource("__RaskConventions");
        Assert.DoesNotContain("Broadcasts", conventions, StringComparison.Ordinal);
    }

    [Fact]
    public void Declaring_Never_is_registered_too()
    {
        // Stating the default explicitly must not be mistaken for saying nothing: an entity that turns the
        // announcement back off should say so in the registry rather than fall through to the same answer by luck.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                public const Broadcasts Broadcast = Broadcasts.Never;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var conventions = run.GeneratedSource("__RaskConventions");
        Assert.Contains(
            "Declare(typeof(global::Shop.Order), (global::Rask.Data.Broadcasts)0)",
            conventions,
            StringComparison.Ordinal);
    }
}
