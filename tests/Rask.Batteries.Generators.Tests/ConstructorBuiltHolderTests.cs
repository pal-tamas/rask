using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK103: a value object EF Core can only build through a constructor taking another value object of several
/// values. EF Core refuses that model when the app starts, so the build refuses it first — and only that shape:
/// each case here is pinned against a real model in <c>Rask.Data.Tests</c>'s <c>ConstructorBuiltHolderTests</c>.
/// </summary>
public class ConstructorBuiltHolderTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations.Schema;
        using Rask.Data;
        namespace Shop;
        public sealed record Sku(string Value);
        public sealed record Label(string Code, int Copies);
        public readonly record struct Pair(int A, int B);

        """;

    private const string Parcel = """

        public sealed class Parcel : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IDeletable
        {
            private Parcel() { }
            public Route Route { get; private set; } = null!;
        }
        """;

    private static List<Diagnostic> Rask103(string source) =>
        [.. GeneratorHarness.Run(
                Usings + source, new ModelRegistryGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore")
            .Diagnostics.Where(d => d.Id == "RASK103")];

    [Fact]
    public void A_record_holding_a_value_object_of_several_values_is_an_error_at_the_parameter_with_the_way_out()
    {
        const string source = "public sealed record Route(Label Label, string Carrier);" + Parcel;

        var diagnostic = Assert.Single(Rask103(source));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "'Route' is built through its constructor and holds 'Label', a value object of several values; EF Core "
            + "cannot bind it. Give 'Route' a private parameterless constructor and private setters.",
            diagnostic.GetMessage());
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal((8, 33, 38), (span.StartLinePosition.Line, span.StartLinePosition.Character, span.EndLinePosition.Character));
    }

    [Theory]
    [InlineData("public sealed record Route(Label? Label, string Carrier);", "Label")]
    [InlineData("public readonly record struct Route(Label Label, string Carrier);", "Label")]
    [InlineData("public sealed record Route(Pair Pair, string Carrier);", "Pair")]
    [InlineData("public sealed record Route(Sku Sku, Label Label);", "Label")]
    [InlineData("public sealed class Route { public Route(Label label, string carrier) { Label = label; Carrier = carrier; } public Label Label { get; private set; } public string Carrier { get; private set; } }", "Label")]
    [InlineData("public sealed class Route { public Route(Label label) { Label = label; } public Route(Label label, string carrier) { Label = label; Carrier = carrier; } public Label Label { get; private set; } public string Carrier { get; private set; } = \"\"; }", "Label")]
    public void Every_shape_whose_every_constructor_takes_one_is_reported(string route, string held)
    {
        var diagnostics = Rask103(route + Parcel);

        var diagnostic = Assert.Single(diagnostics);

        Assert.StartsWith($"'Route' is built through its constructor and holds '{held}',", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed class Route { private Route() { } public Label Label { get; private set; } = null!; public string Carrier { get; private set; } = \"\"; }")]
    [InlineData("public sealed class Route { public Label Label { get; set; } = null!; public string Carrier { get; set; } = \"\"; }")]
    [InlineData("public sealed record Route(Label Label, string Carrier) { private Route() : this(null!, \"\") { } }")]
    [InlineData("public sealed class Route { public Route(string carrier) { Carrier = carrier; } public Label Label { get; private set; } = null!; public string Carrier { get; private set; } }")]
    [InlineData("public struct Route { public Label Label { get; set; } public string Carrier { get; set; } }")]
    [InlineData("public sealed record Route(Sku Sku, string Carrier);")]
    [InlineData("public sealed record Route(Sku? Sku, int Stops);")]
    public void A_shape_EF_Core_can_build_is_not_reported(string route)
    {
        var diagnostics = Rask103(route + Parcel);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void One_reached_through_another_value_object_or_a_row_inside_the_aggregate_is_reported_too()
    {
        var diagnostics = Rask103("""
            public sealed record Route(Label Label, string Carrier);
            public sealed record Leg(Label Label, int Hours);
            public sealed class Hold
            {
                private Hold() { }
                public Route Route { get; private set; } = null!;
                public string Deck { get; private set; } = "";
            }
            public sealed class Parcel : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IDeletable
            {
                private readonly List<Stop> _stops = [];
                private Parcel() { }
                public Hold Hold { get; private set; } = null!;
                public IReadOnlyList<Stop> Stops => _stops;
            }
            public sealed class Stop : Entity<Guid>
            {
                private Stop() { }
                public Leg Leg { get; private set; } = null!;
            }
            """);

        Assert.Equal(
            ["'Leg' is built", "'Route' is built"],
            diagnostics.Select(d => d.GetMessage()[..d.GetMessage().IndexOf(" through", StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void One_that_two_aggregates_hold_is_reported_once()
    {
        var diagnostics = Rask103("""
            public sealed record Route(Label Label, string Carrier);
            public sealed class Parcel : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IDeletable
            {
                private Parcel() { }
                public Route Route { get; private set; } = null!;
                public Route Return { get; private set; } = null!;
            }
            public sealed class Pallet : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IDeletable
            {
                private Pallet() { }
                public Route Route { get; private set; } = null!;
            }
            """);

        Assert.Single(diagnostics);
    }

    [Fact]
    public void What_the_mapping_never_reaches_is_not_reported()
    {
        var diagnostics = Rask103("""
            public sealed record Route(Label Label, string Carrier);
            public sealed class Parcel : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IDeletable
            {
                private readonly List<Route> _routes = [];
                private Parcel() { }
                [NotMapped] public Route? Planned { get; private set; }
                public IReadOnlyList<Route> Routes => _routes;
                public string Title { get; private set; } = "";
            }
            public sealed class Notebook
            {
                public Route Route { get; set; } = null!;
            }
            """);

        Assert.Empty(diagnostics);
    }
}
