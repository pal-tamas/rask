using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Data.Generators;

namespace Rask.Data.Generators.Tests;

/// <summary>
/// Drives <see cref="ModelInputGenerator"/> over hand-written entities.
/// </summary>
/// <remarks>
/// Every case compiles the emitted source back into the compilation: a model, or a nested value-object model,
/// that names a type wrongly does not compile — and that is the failure a green generator build would otherwise
/// hide. The generator emits the form model and nothing else: no writes, no reads into it, no accessors.
/// </remarks>
public class ModelInputGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ModelInputGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    // The generated trees' own warnings — a nullable mismatch in a nested model is only a warning, so
    // GeneratedCompileErrors cannot see it, and an app building warnings-as-errors fails on it.
    private static IReadOnlyList<Diagnostic> GeneratedWarnings(GeneratorRun run)
    {
        var generated = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => CSharpSyntaxTree.ParseText(
                s.SourceText, new CSharpParseOptions(LanguageVersion.Latest), path: s.HintName))
            .ToArray();

        return run.Compilation.AddSyntaxTrees(generated)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning && generated.Contains(d.Location.SourceTree))
            .ToList();
    }

    [Fact]
    public void An_entity_gets_a_form_model_carrying_its_validation_attributes()
    {
        var run = Run("""
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>, ITimestamped, ISoftDeletable, IVersioned
            {
                private Product() { }
                [Required, MaxLength(200), Column("product_name")] public string Name { get; private set; } = "";
                public decimal Price { get; set; }
                public string? Notes { get; private set; }
                [ConcurrencyCheck] public string Stamp { get; private set; } = "";
                [Timestamp] public byte[] RowVersion { get; private set; } = [];
                public DateTime CreatedAt { get; private set; }
                public DateTime UpdatedAt { get; private set; }
                public DateTime? DeletedAt { get; private set; }
                public int Version { get; private set; }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(GeneratedWarnings(run));

        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.Contains("public sealed partial class ProductModel", source, StringComparison.Ordinal);
        Assert.Contains("public string Name { get; set; } = \"\";", source, StringComparison.Ordinal);
        Assert.Contains("[global::System.ComponentModel.DataAnnotations.RequiredAttribute()]", source, StringComparison.Ordinal);
        Assert.Contains("[global::System.ComponentModel.DataAnnotations.MaxLengthAttribute(200)]", source, StringComparison.Ordinal);
        Assert.Contains("public decimal Price { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public string? Notes { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public int Version { get; set; }", source, StringComparison.Ordinal);

        // Schema and concurrency attributes say nothing to a form.
        Assert.DoesNotContain("ColumnAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrencyCheckAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimestampAttribute", source, StringComparison.Ordinal);

        // The columns the interceptors own, and the events buffer, are the framework's.
        Assert.DoesNotContain("CreatedAt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletedAt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DomainEvents", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_model_is_generated_no_writes_no_ToModel_and_no_accessors()
    {
        // The writes, ToModel() and the [UnsafeAccessor] plumbing that served them are gone. A model with no
        // parameterless constructor — which once cost it CreateAsync and a RASK081 — gets its model like any other.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly record struct ProductId(Guid Value);
            public sealed class Product : Model<Guid>, IVersioned
            {
                public Product(string name) => Name = name;
                public string Name { get; private set; }
                public int Version { get; private set; }
            }
            public sealed class Tag : Model<ProductId>
            {
                private Tag() { }
                public string Label { get; private set; } = "";
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        foreach (var source in new[] { run.GeneratedSource("Shop.ProductModel"), run.GeneratedSource("Shop.TagModel") })
        {
            Assert.DoesNotContain("CreateAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DeleteAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ToModel", source, StringComparison.Ordinal);
            Assert.DoesNotContain("UnsafeAccessor", source, StringComparison.Ordinal);
            Assert.DoesNotContain("static class", source, StringComparison.Ordinal);
        }

        Assert.Contains("public string Name { get; set; } = \"\";", run.GeneratedSource("Shop.ProductModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_carries_no_id()
    {
        // Overposting: a key on the model is a key the client chooses, and one edited field would re-point the
        // write at another row. The id is only ever the caller's — the route, or the entity it loaded.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>
            {
                private Product() { }
                public string Name { get; private set; } = "";
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.DoesNotContain(" Id { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("It carries no id", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SkipModel_leaves_a_property_off_and_on_the_class_generates_nothing()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>
            {
                private Product() { }
                public string Name { get; private set; } = "";
                [SkipModel] public int Views { get; private set; }
            }

            [SkipModel]
            public sealed class AuditRow : Model<Guid>
            {
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("Views", run.GeneratedSource("Shop.ProductModel"), StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource("AuditRowModel"));
    }

    [Fact]
    public void Value_objects_become_nested_models()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed record Money(decimal Amount, string Currency) : IValueObject;
            public sealed class Packaging : IValueObject
            {
                public Money Cost { get; set; } = new(0m, "EUR");
                public string Material { get; set; } = "card";
            }
            public sealed class Order : Model<Guid>
            {
                private Order() { }
                public Money Total { get; private set; } = new(0m, "EUR");
                public Packaging? Box { get; private set; }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(GeneratedWarnings(run));

        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("public sealed partial class MoneyModel", source, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class PackagingModel", source, StringComparison.Ordinal);
        Assert.Contains("public MoneyModel Total { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public PackagingModel? Box { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public MoneyModel Cost { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public decimal Amount { get; set; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Value_objects_with_non_public_constructors_and_setters_still_get_mutable_nested_models()
    {
        // However the value object keeps itself — a private constructor, private setters, a struct — its nested
        // model is the same settable class a form binds into.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Address : IValueObject
            {
                private Address() { }
                public string Street { get; private set; } = "";
                public string? City { get; private set; }
            }
            public sealed class Weight : IValueObject
            {
                private Weight(decimal amount, string unit) { Amount = amount; Unit = unit; }
                public decimal Amount { get; }
                public string Unit { get; }
                public static Weight Of(decimal amount, string unit) => new(amount, unit);
            }
            public struct Dimensions : IValueObject
            {
                public decimal Width { get; private set; }
                public decimal Height { get; private set; }
            }
            public sealed class Shipment : Model<Guid>
            {
                private Shipment() { }
                public Address Destination { get; private set; } = null!;
                public Address? ReturnTo { get; private set; }
                public Weight Weight { get; private set; } = null!;
                public Dimensions Size { get; private set; }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(GeneratedWarnings(run));

        var source = run.GeneratedSource("Shop.ShipmentModel");
        Assert.Contains("public sealed partial class AddressModel", source, StringComparison.Ordinal);
        Assert.Contains("public string Street { get; set; } = \"\";", source, StringComparison.Ordinal);
        Assert.Contains("public string? City { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public AddressModel Destination { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public AddressModel? ReturnTo { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class WeightModel", source, StringComparison.Ordinal);
        Assert.Contains("public string Unit { get; set; } = \"\";", source, StringComparison.Ordinal);
        Assert.Contains("public WeightModel Weight { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public DimensionsModel Size { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public decimal Width { get; set; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Properties_declared_on_a_constrained_generic_base_are_carried()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public abstract class ValueKeyed<TId> : Model<TId> where TId : struct
            {
                public string Name { get; private set; } = "";
            }
            public abstract class NotNullKeyed<TId> : Model<TId> where TId : notnull
            {
                public string Code { get; private set; } = "";
            }
            public abstract class ReferenceKeyed<TKey> : Model<TKey> where TKey : class
            {
                public string Label { get; private set; } = "";
            }
            public abstract class Ranked<TId, TSelf> : Model<TId>
                where TId : unmanaged, IComparable<TId>
                where TSelf : Ranked<TId, TSelf>, new()
            {
                public int Rank { get; private set; }
            }
            public sealed class Product : ValueKeyed<Guid> { private Product() { } }
            public sealed class Voucher : NotNullKeyed<string> { private Voucher() { } }
            public sealed class Slug : ReferenceKeyed<string> { private Slug() { } }
            public sealed class Level : Ranked<int, Level> { public Level() { } }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(GeneratedWarnings(run));

        Assert.Contains("public string Name { get; set; } = \"\";", run.GeneratedSource("Shop.ProductModel"), StringComparison.Ordinal);
        Assert.Contains("public string Code { get; set; } = \"\";", run.GeneratedSource("Shop.VoucherModel"), StringComparison.Ordinal);
        Assert.Contains("public string Label { get; set; } = \"\";", run.GeneratedSource("Shop.SlugModel"), StringComparison.Ordinal);
        Assert.Contains("public int Rank { get; set; }", run.GeneratedSource("Shop.LevelModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_value_objects_sharing_a_name_get_a_nested_model_each()
    {
        // Keyed by simple name, Billing.Money's model was dropped in favour of Shop.Money's, and Fee was
        // typed as a class that did not have its members.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Billing
            {
                public sealed record Money(long Cents) : IValueObject;
            }
            namespace Shop
            {
                public sealed record Money(decimal Amount, string Currency) : IValueObject;
                public sealed class Order : Model<Guid>
                {
                    private Order() { }
                    public Money Total { get; private set; } = new(0m, "EUR");
                    public Billing.Money Fee { get; private set; } = new(0);
                }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("public MoneyModel Total { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public MoneyModel2 Fee { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public long Cents { get; set; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_object_reached_by_two_paths_has_one_shape()
    {
        // D sits at the depth limit under Deep, where its Leaf is copied as a value, and at the top under
        // Direct, where Leaf would be a nested model. One DModel is emitted, so both uses must be that one.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed record Leaf(string Code) : IValueObject;
            public sealed record D(Leaf Leaf) : IValueObject;
            public sealed record C(D D) : IValueObject;
            public sealed record B(C C) : IValueObject;
            public sealed record A(B B) : IValueObject;
            public sealed class Order : Model<Guid>
            {
                private Order() { }
                public A Deep { get; private set; } = null!;
                public D Direct { get; private set; } = null!;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("public DModel Direct { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, "partial class DModel\\b"));
    }

    [Fact]
    public void An_attribute_taking_an_array_is_copied_as_one()
    {
        var run = Run("""
            using System;
            using System.ComponentModel.DataAnnotations;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>
            {
                private Product() { }
                [AllowedValues("draft", "live")] public string Status { get; private set; } = "draft";
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("AllowedValuesAttribute(", run.GeneratedSource("Shop.ProductModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void Navigations_collections_and_computed_properties_are_left_off_but_stored_ones_are_carried()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Customer : Model<Guid>
            {
                private Customer() { }
            }
            public sealed class Order : Model<Guid>
            {
                private Order() { }
                public Customer Customer { get; private set; } = null!;
                public Guid CustomerId { get; private set; }
                public List<string> Tags { get; private set; } = new();
                public string Label => "#" + Id;
                public string Code { get; } = "";
                public int Quantity { get; init; }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("public global::System.Guid CustomerId { get; set; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public global::Shop.Customer Customer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tags", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Label", source, StringComparison.Ordinal);
        Assert.Contains("public string Code { get; set; } = \"\";", source, StringComparison.Ordinal);
        Assert.Contains("public int Quantity { get; set; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_internal_entity_gets_an_internal_model_and_a_global_one_compiles()
    {
        var run = Run("""
            using Rask.Data;
            internal sealed class Note : Model<int>
            {
                private Note() { }
                public string Text { get; private set; } = "";
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("NoteModel");
        Assert.Contains("internal sealed partial class NoteModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_written_model_of_the_same_name_is_RASK082_and_nothing_is_generated()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>
            {
                private Product() { }
            }
            public sealed class ProductModel
            {
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK082", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(run.HasGeneratedSource("ProductModel"));
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_partial_model_of_the_same_name_extends_the_generated_one()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>
            {
                private Product() { }
                public string Name { get; private set; } = "";
            }
            public sealed partial class ProductModel
            {
                public string Shouted => Name.ToUpperInvariant();
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.True(run.HasGeneratedSource("Shop.ProductModel"));
    }

    [Fact]
    public void A_nested_entity_is_RASK083_and_gets_no_model()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public static class Catalog
            {
                public sealed class Product : Model<Guid>
                {
                    private Product() { }
                }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK083", diagnostic.Id);
        Assert.False(run.HasGeneratedSource("ProductModel"));
        Assert.Empty(run.GeneratedCompileErrors());
    }
}
