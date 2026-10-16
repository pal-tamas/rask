namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK105: what an aggregate holds as a value object must compare by value — a record, a record struct, or a plain
/// class on <c>Rask.Data.ValueObject</c>. A strongly-typed id is a column, not a value object, and is left alone.
/// </summary>
public class ReferenceValueGeneratorTests
{
    private static GeneratorRun Run(string valueObject) =>
        GeneratorHarness.Run(
            $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;

            {{valueObject}}

            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public Money Total { get; private set; } = null!;
            }
            """,
            new ModelRegistryGenerator(),
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_plain_class_held_as_a_value_object_is_RASK105()
    {
        var run = Run("""
            public sealed class Money
            {
                private Money() { }
                public decimal Amount { get; private set; }
                public string Currency { get; private set; } = "";
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics, static d => d.Id == "RASK105");

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Money' is held as a value object on 'Order'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed record Money(decimal Amount, string Currency);")]
    [InlineData("public readonly record struct Money(decimal Amount, string Currency);")]
    [InlineData("""
        public sealed class Money : ValueObject
        {
            private Money() { }
            public decimal Amount { get; private set; }
            public string Currency { get; private set; } = "";
            protected override IEnumerable<object?> EqualityComponents() { yield return Amount; yield return Currency; }
        }
        """)]
    public void A_value_object_that_compares_by_value_is_fine(string valueObject)
    {
        var run = Run(valueObject);

        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "RASK105");
    }

    [Fact]
    public void A_strongly_typed_id_another_aggregate_is_keyed_by_is_not_a_value_object()
    {
        var run = GeneratorHarness.Run(
            """
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly struct CustomerId(Guid value) { public Guid Value { get; } = value; }
            public sealed class Customer : Aggregate<CustomerId> { private Customer() { } }
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public CustomerId Customer { get; private set; }
            }
            """,
            new ModelRegistryGenerator(),
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

        var reported = run.Diagnostics.Any(static d => d.Id == "RASK105");

        Assert.False(reported);
    }
}
