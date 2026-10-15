namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// One child type held by two roots — a chip a driver or a vehicle holds — with no tenant property of its own (#1251).
/// </summary>
public class ChildOfTwoRootsGeneratorTests
{
    private const string Fleet = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Fleet;

        public sealed class Driver : Aggregate<string>
        {
            private readonly List<Chip> _chips = [];
            private Driver() { }
            public const Tenancy Scope = Tenancy.PerTenant;
            public Guid? TenantId { get; private set; }
            public string Name { get; private set; } = "";
            public IReadOnlyList<Chip> Chips => _chips;
        }

        public sealed class Vehicle : Aggregate<string>
        {
            private readonly List<Chip> _chips = [];
            private Vehicle() { }
            public const Tenancy Scope = Tenancy.Shared;
            public string Plate { get; private set; } = "";
            public IReadOnlyList<Chip> Chips => _chips;
        }

        public sealed class Chip : Entity<string>
        {
            private Chip() { }
            public string Code { get; private set; } = "";
            public string? DriverId { get; private set; }
            public string? VehicleId { get; private set; }
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Fleet,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

    [Fact]
    public void The_childs_read_face_has_an_optional_navigation_back_to_each_root()
    {
        var run = Run();

        var chip = run.GeneratedSource("Fleet_ChipRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public global::Fleet.DriverRead? Driver { get; init; }", chip, StringComparison.Ordinal);
        Assert.Contains("public global::Fleet.VehicleRead? Vehicle { get; init; }", chip, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantId", chip, StringComparison.Ordinal);
    }

    [Fact]
    public void The_child_is_partitioned_when_either_root_is_whichever_is_read_last()
    {
        var conventions = Run().GeneratedSource("__RaskConventions");

        Assert.Contains(
            "global::Rask.Data.ConventionRegistry.Declare(typeof(global::Fleet.Chip), (global::Rask.Data.Tenancy)1);",
            conventions,
            StringComparison.Ordinal);
    }
}
