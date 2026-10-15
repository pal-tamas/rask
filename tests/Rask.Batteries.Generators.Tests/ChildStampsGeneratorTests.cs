namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// Timestamps are the aggregate's, like its tenant: a child entity carries none unless it declares its own Stamps —
/// not on its read face, not in its registered conventions.
/// </summary>
public class ChildStampsGeneratorTests
{
    private const string Declares = "global::Rask.Data.ConventionRegistry.Declare(typeof(global::Fleet.";

    [Fact]
    public void A_child_and_grandchild_that_declare_nothing_have_no_timestamps_on_their_faces()
    {
        var run = Run();

        var chip = run.GeneratedSource("Fleet_ChipRead");
        var seal = run.GeneratedSource("Fleet_SealRead");
        var conventions = run.GeneratedSource("__RaskConventions");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("CreatedAt", chip, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAt", chip, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", seal, StringComparison.Ordinal);
        Assert.Contains(Declares + "Chip), (global::Rask.Data.Timestamps)0);", conventions, StringComparison.Ordinal);
        Assert.Contains(Declares + "Seal), (global::Rask.Data.Timestamps)0);", conventions, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", run.GeneratedSource("Fleet.ChipModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_root_keeps_both_timestamps_and_registers_nothing_for_them()
    {
        var run = Run();

        var driver = run.GeneratedSource("Fleet_DriverRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" CreatedAt {", driver, StringComparison.Ordinal);
        Assert.Contains(" UpdatedAt {", driver, StringComparison.Ordinal);
        Assert.DoesNotContain(Declares + "Driver), (global::Rask.Data.Timestamps)", run.GeneratedSource("__RaskConventions"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_child_that_declares_its_own_keeps_it()
    {
        var run = Run("public const Timestamps Stamps = Timestamps.All;");

        var chip = run.GeneratedSource("Fleet_ChipRead");
        var conventions = run.GeneratedSource("__RaskConventions");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" CreatedAt {", chip, StringComparison.Ordinal);
        Assert.Contains(" UpdatedAt {", chip, StringComparison.Ordinal);
        Assert.Contains(Declares + "Chip), (global::Rask.Data.Timestamps)3);", conventions, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", run.GeneratedSource("Fleet_SealRead"), StringComparison.Ordinal);
    }

    private static GeneratorRun Run(string chipStamps = "") =>
        GeneratorHarness.Run(
            $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Fleet;

            public sealed class Driver : Aggregate<string>
            {
                private readonly List<Chip> _chips = [];
                private Driver() { }
                public string Name { get; private set; } = "";
                public IReadOnlyList<Chip> Chips => _chips;
            }

            public sealed class Chip : Entity<string>
            {
                private readonly List<Seal> _seals = [];
                private Chip() { }
                {{chipStamps}}
                public string Code { get; private set; } = "";
                public IReadOnlyList<Seal> Seals => _seals;
            }

            public sealed class Seal : Entity<string>
            {
                private Seal() { }
                public string Mark { get; private set; } = "";
            }
            """,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");
}
