using Microsoft.CodeAnalysis;
using Rask.Data.Generators.Analyzers;

namespace Rask.Data.Generators.Tests;

/// <summary>RASK084: an entity's or a value object's state can be changed from outside the type.</summary>
public class ModelStateMutationAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Run(string body) =>
        ModelStateAnalyzerHarness.RunAsync(new ModelStateMutationAnalyzer(), $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            {{body}}
            """);

    [Fact]
    public async Task A_public_setter_on_an_entity_is_reported_at_the_accessor()
    {
        var diagnostic = Assert.Single(await Run("""
            public sealed class Product : Aggregate<Guid>
            {
                public string Name { get; set; } = "";
            }
            """));

        Assert.Equal("RASK084", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("set;", diagnostic.Flagged());
        Assert.Contains("'Product.Name' has a public setter", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("make it 'private set'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("through the methods of 'Product' (or its constructor)", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_public_accessors_are_not_reported()
    {
        Assert.Empty(await Run("""
            public sealed class Product : Aggregate<Guid>
            {
                public string Name { get; private set; } = "";
                public string Sku { get; internal set; } = "";
                public decimal Price { get; protected set; }
                public string Slug { get; private init; } = "";
                public int Stock { get; }
                internal string Internal { get; set; } = "";
                private string Secret { get; set; } = "";
            }
            """));
    }

    [Fact]
    public async Task A_public_init_on_an_entity_is_reported()
    {
        var diagnostic = Assert.Single(await Run("""
            public sealed class Product : Aggregate<Guid>
            {
                public string Name { get; init; } = "";
            }
            """));

        Assert.Equal("init;", diagnostic.Flagged());
        Assert.Contains("has a public init accessor", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'private init'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_public_setter_on_an_abstract_base_between_Model_and_the_entity_is_reported()
    {
        var diagnostic = Assert.Single(await Run("""
            public abstract class Audited : Aggregate<Guid>
            {
                public string CreatedBy { get; set; } = "";
            }
            public sealed class Product : Audited { }
            """));

        Assert.Contains("'Audited.CreatedBy'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_protected_setter_on_an_abstract_entity_base_is_not_reported()
    {
        Assert.Empty(await Run("""
            public abstract class Audited : Aggregate<Guid>
            {
                public string CreatedBy { get; protected set; } = "";
            }
            public sealed class Product : Audited
            {
                public void Stamp(string by) => CreatedBy = by;
            }
            """));
    }

    [Fact]
    public async Task The_inherited_Id_from_Model_is_never_reported()
    {
        // Aggregate<TId>.Id is `protected set` and lives in metadata; only a type's OWN members are judged.
        Assert.Empty(await Run("""
            public sealed class Product : Aggregate<Guid>
            {
                public static Product Create() => new() { Id = Guid.NewGuid() };
            }
            """));
    }

    [Fact]
    public async Task A_composite_no_aggregate_holds_is_not_a_value_object_and_is_not_reported()
    {
        Assert.Empty(await Run("""
            public sealed class Address
            {
                public string City { get; set; } = "";
            }
            """));
    }

    [Fact]
    public async Task A_type_that_is_not_an_entity_or_a_value_object_is_not_reported()
    {
        Assert.Empty(await Run("""
            public sealed class ProductForm
            {
                public string Name { get; set; } = "";
                public int Count;
            }
            public sealed record Dto(string Name);
            """));
    }

    [Fact]
    public async Task A_positional_record_value_object_is_exempt()
    {
        Assert.Empty(await Run("""
            public sealed record Money(decimal Amount, string Currency);
            public readonly record struct Weight(decimal Grams);
            """));
    }

    [Fact]
    public async Task An_explicit_init_on_a_value_object_is_reported()
    {
        var diagnostic = Assert.Single(await Run("""
            public sealed record Address
            {
                public string City { get; init; } = "";
            }
            public sealed class Customer : Aggregate<Guid>
            {
                public Address Home { get; private set; } = new();
            }
            """));

        Assert.Equal("init;", diagnostic.Flagged());
        Assert.Contains("'Address.City' has a public init accessor", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_public_setter_on_a_value_object_is_reported()
    {
        Assert.Equal("set;", Assert.Single(await Run("""
            public sealed class Address
            {
                public string City { get; set; } = "";
            }
            public sealed class Customer : Aggregate<Guid>
            {
                public Address Home { get; private set; } = new();
            }
            """)).Flagged());
    }

    [Fact]
    public async Task A_positional_parameter_of_a_mutable_record_struct_value_object_is_reported_at_the_parameter()
    {
        // A non-readonly record struct gives its positional properties a real `set`, so the exemption is for
        // the init accessor only.
        var diagnostic = Assert.Single(await Run("""
            public record struct Weight(decimal Grams);
            public sealed class Parcel : Aggregate<Guid>
            {
                public Weight Weight { get; private set; }
            }
            """));

        Assert.Equal("decimal Grams", diagnostic.Flagged());
        Assert.Contains("readonly record struct", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.True(diagnostic.Properties.ContainsKey(ModelTypes.NoFixProperty));
    }

    [Fact]
    public async Task A_public_mutable_field_is_reported_and_a_readonly_one_is_not()
    {
        var diagnostic = Assert.Single(await Run("""
            public sealed class Product : Aggregate<Guid>
            {
                public int Stock;
                public readonly int Limit = 10;
                public const int Max = 99;
                public static int Created;
                private int _hidden;
                public int Hidden => _hidden;
            }
            """));

        Assert.Equal("Stock", diagnostic.Flagged());
        Assert.Contains("'Product.Stock' is a public field that is not readonly", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_public_mutable_field_on_a_value_object_struct_is_reported()
    {
        Assert.Equal("Amount = 0", Assert.Single(await Run("""
            public struct Money
            {
                public decimal Amount = 0;
                public Money() { }
                public string Currency { get; private set; } = "EUR";
            }
            public sealed class Invoice : Aggregate<Guid>
            {
                public Money Total { get; private set; }
            }
            """)).Flagged());
    }

    [Fact]
    public async Task Every_part_of_a_partial_entity_is_judged()
    {
        var diagnostics = await Run("""
            public sealed partial class Product : Aggregate<Guid>
            {
                public string Name { get; set; } = "";
            }
            public sealed partial class Product
            {
                public string Sku { get; set; } = "";
            }
            """);

        Assert.Equal(2, diagnostics.Count);
    }

    [Fact]
    public async Task An_override_is_judged_at_its_base_not_again()
    {
        var diagnostic = Assert.Single(await Run("""
            public abstract class Named : Aggregate<Guid>
            {
                public virtual string Name { get; set; } = "";
            }
            public sealed class Product : Named
            {
                public override string Name { get; set; } = "";
            }
            """));

        Assert.Contains("'Named.Name'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public required string Name { get; set; }")]
    [InlineData("public abstract string Name { get; set; }")]
    [InlineData("public string Name { private get; set; } = \"\";")]
    [InlineData("private string _hash = \"\"; public string Hash => _hash; public string Password { set => _hash = value; }")]
    public async Task The_fix_is_withheld_where_private_would_not_compile(string member)
    {
        var diagnostic = Assert.Single(await Run($$"""
            public abstract class Product : Aggregate<Guid>
            {
                {{member}}
            }
            """));

        Assert.True(diagnostic.Properties.ContainsKey(ModelTypes.NoFixProperty));
    }

    [Fact]
    public async Task The_fix_is_withheld_for_a_setter_an_interface_demands()
    {
        var diagnostic = Assert.Single(await Run("""
            public interface INamed { string Name { get; set; } }
            public sealed class Product : Aggregate<Guid>, INamed
            {
                public string Name { get; set; } = "";
            }
            """));

        Assert.True(diagnostic.Properties.ContainsKey(ModelTypes.NoFixProperty));
    }

    [Fact]
    public async Task An_ordinary_public_setter_carries_no_fix_veto()
    {
        var diagnostic = Assert.Single(await Run("""
            public sealed class Product : Aggregate<Guid>
            {
                public string Name { get; set; } = "";
            }
            """));

        Assert.False(diagnostic.Properties.ContainsKey(ModelTypes.NoFixProperty));
    }
}
