using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Data.Generators;

namespace Rask.Data.Generators.Tests;

/// <summary>
/// Drives <see cref="ModelInputGenerator"/> over hand-written entities.
/// </summary>
/// <remarks>
/// Every case compiles the emitted source back into the compilation: an <c>[UnsafeAccessor]</c> whose
/// signature is wrong still compiles, but a model, an extension block or an accessor that names a type
/// wrongly does not — and that is the failure a green generator build would otherwise hide. Whether an
/// accessor finds its member at RUNTIME is Rask.Data.Tests' question.
/// </remarks>
public class ModelInputGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ModelInputGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    // The generated trees' own warnings. A `notnull` constraint missing from an accessor is CS8714 — a warning,
    // so GeneratedCompileErrors cannot see it, and an app building warnings-as-errors fails on it.
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
    public void An_entity_gets_a_model_and_the_writes_that_take_it()
    {
        var run = Run("""
            using System;
            using System.ComponentModel.DataAnnotations;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>, ITimestamped, IVersioned
            {
                private Product() { }
                [Required, MaxLength(200)] public string Name { get; private set; } = "";
                public decimal Price { get; set; }
                public string? Notes { get; private set; }
                public DateTime CreatedAt { get; private set; }
                public int Version { get; private set; }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.Contains("public sealed partial class ProductModel", source, StringComparison.Ordinal);
        Assert.Contains("public string Name { get; set; } = \"\";", source, StringComparison.Ordinal);
        Assert.Contains("[global::System.ComponentModel.DataAnnotations.RequiredAttribute()]", source, StringComparison.Ordinal);
        Assert.Contains("[global::System.ComponentModel.DataAnnotations.MaxLengthAttribute(200)]", source, StringComparison.Ordinal);
        Assert.Contains("public string? Notes { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public int Version { get; set; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", source, StringComparison.Ordinal);

        Assert.Contains("CreateAsync(global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        Assert.Contains("CreateAsync(global::System.Guid id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::System.Guid id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        Assert.Contains("DeleteAsync(global::System.Guid id, int? version = null, ", source, StringComparison.Ordinal);
        Assert.Contains("public global::Shop.ProductModel ToModel()", source, StringComparison.Ordinal);
        Assert.Contains(
            "<c>Product.CreateAsync(model)</c>, <c>Product.CreateAsync(id, model)</c> or <c>Product.UpdateAsync(id, model)</c>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_carries_no_id_so_an_update_takes_it_beside_the_model_and_a_create_takes_none()
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
        Assert.DoesNotContain("model.Id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Id = entity.Id", source, StringComparison.Ordinal);
        Assert.Contains(".UpdateAsync<global::Shop.Product>(id!, null, entity => __Apply(entity, model), cancellationToken);", source, StringComparison.Ordinal);
        Assert.Contains("<param name=\"id\">The id of the row to update.</param>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_without_a_version_deletes_by_id_alone()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Tag : Model<Guid>
            {
                private Tag() { }
                public string Label { get; private set; } = "";
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("Shop.TagModel");
        Assert.Contains(
            "DeleteAsync(global::System.Guid id, global::System.Threading.CancellationToken cancellationToken = default)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("int? version", source, StringComparison.Ordinal);
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
    public void A_strongly_typed_guid_id_is_the_key_parameter_and_is_assigned_on_create()
    {
        // EF Core does not generate a key behind a value converter, so the create assigns one — only when the
        // constructor left it unset — through a generic accessor into Model<TId>.Id.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly record struct ProductId(Guid Value);
            public sealed class Product : Model<ProductId>
            {
                private Product() { }
                public string Name { get; private set; } = "";
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.Contains("DeleteAsync(global::Shop.ProductId id, ", source, StringComparison.Ordinal);
        Assert.Contains("CreateAsync(global::Shop.ProductId id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::Shop.ProductId id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        Assert.Contains(
            "if (global::System.Collections.Generic.EqualityComparer<global::Shop.ProductId>.Default.Equals(entity.Id, default!))",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "__SetterKey_Id<global::Shop.ProductId>.Invoke(entity, new global::Shop.ProductId(global::System.Guid.CreateVersion7()));",
            source,
            StringComparison.Ordinal);
        Assert.Contains("private static class __SetterKey_Id<TId>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_less_create_exists_only_where_a_key_can_be_produced_without_the_caller()
    {
        // A Guid (or a strongly-typed id over one) is generated here; an integer by the store's identity. Nothing
        // produces a string, or a strongly-typed id over an integer or a string, so the caller must say it.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly record struct ProductId(Guid Value);
            public readonly record struct OrderNumber(long Value);
            public readonly record struct Sku(string Value);
            public sealed class Product : Model<ProductId> { private Product() { } }
            public sealed class Tag : Model<Guid> { private Tag() { } }
            public sealed class Note : Model<int> { private Note() { } }
            public sealed class Entry : Model<long> { private Entry() { } }
            public sealed class Order : Model<OrderNumber> { private Order() { } }
            public sealed class Item : Model<Sku> { private Item() { } }
            public sealed class Country : Model<string> { private Country() { } }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        foreach (var (name, id) in new[]
                 {
                     ("Product", "global::Shop.ProductId"), ("Tag", "global::System.Guid"), ("Note", "int"), ("Entry", "long"),
                 })
        {
            var source = run.GeneratedSource("Shop." + name + "Model");
            Assert.Contains($"CreateAsync(global::Shop.{name}Model model, ", source, StringComparison.Ordinal);
            Assert.Contains($"CreateAsync({id} id, global::Shop.{name}Model model, ", source, StringComparison.Ordinal);
        }

        foreach (var (name, id) in new[]
                 {
                     ("Order", "global::Shop.OrderNumber"), ("Item", "global::Shop.Sku"), ("Country", "string"),
                 })
        {
            var source = run.GeneratedSource("Shop." + name + "Model");
            Assert.DoesNotContain($"CreateAsync(global::Shop.{name}Model model, ", source, StringComparison.Ordinal);
            Assert.DoesNotContain($"<c>{name}.CreateAsync(model)</c>", source, StringComparison.Ordinal);
            Assert.Contains($"CreateAsync({id} id, global::Shop.{name}Model model, ", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateVersion7", source, StringComparison.Ordinal);
        }

        // An integer key is left to the store — nothing is assigned — and a Guid key is assigned only when unset.
        Assert.DoesNotContain("CreateVersion7", run.GeneratedSource("Shop.NoteModel"), StringComparison.Ordinal);
        Assert.DoesNotContain("CreateVersion7", run.GeneratedSource("Shop.EntryModel"), StringComparison.Ordinal);
        Assert.Contains(
            "__SetterKey_Id<global::System.Guid>.Invoke(entity, global::System.Guid.CreateVersion7());",
            run.GeneratedSource("Shop.TagModel"),
            StringComparison.Ordinal);

        // The id overload takes the key as given, and refuses a null one for a reference key.
        Assert.Contains("__SetterKey_Id<int>.Invoke(entity, id);", run.GeneratedSource("Shop.NoteModel"), StringComparison.Ordinal);
        Assert.Contains("global::System.ArgumentNullException.ThrowIfNull(id);", run.GeneratedSource("Shop.CountryModel"), StringComparison.Ordinal);
        Assert.DoesNotContain("ThrowIfNull(id)", run.GeneratedSource("Shop.TagModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void Value_objects_become_nested_models_rebuilt_by_constructor_or_initializer()
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

        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("public sealed partial class MoneyModel", source, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class PackagingModel", source, StringComparison.Ordinal);
        Assert.Contains("public MoneyModel Total { get; set; } = new();", source, StringComparison.Ordinal);
        Assert.Contains("public PackagingModel? Box { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("new global::Shop.Money(model.Total.Amount, model.Total.Currency)", source, StringComparison.Ordinal);
        Assert.Contains("new global::Shop.Packaging { Cost = ", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Value_objects_with_non_public_constructors_and_setters_are_rebuilt_through_accessors()
    {
        // What RASK084 steers a value object towards: no public setters. A private constructor naming every
        // property is called through an accessor; a private parameterless one is followed by each property's
        // private setter; a struct is written by reference, or the accessor would write into a copy.
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

        // The nested models are the same mutable classes as ever, whatever the value object's own shape.
        Assert.Contains("public sealed partial class AddressModel", source, StringComparison.Ordinal);
        Assert.Contains("public string Street { get; set; } = \"\";", source, StringComparison.Ordinal);

        // Private parameterless constructor, then private setters.
        Assert.Contains("private static extern global::Shop.Address __NewAddressModel();", source, StringComparison.Ordinal);
        Assert.Contains("private static global::Shop.Address __BuildAddressModel(string p0, string? p1)", source, StringComparison.Ordinal);
        Assert.Contains("__SetterAddressModel_Street(value, p0);", source, StringComparison.Ordinal);
        Assert.Contains("__BuildAddressModel(model.Destination.Street, model.Destination.City)", source, StringComparison.Ordinal);
        Assert.Contains("(model.ReturnTo is null ? null : __BuildAddressModel(model.ReturnTo.Street, model.ReturnTo.City))", source, StringComparison.Ordinal);

        // Private constructor naming every property.
        Assert.Contains("private static extern global::Shop.Weight __NewWeightModel(decimal p0, string p1);", source, StringComparison.Ordinal);
        Assert.Contains("__NewWeightModel(model.Weight.Amount, model.Weight.Unit)", source, StringComparison.Ordinal);

        // A struct: its own public constructor, then setters reached by reference.
        Assert.Contains("var value = new global::Shop.Dimensions();", source, StringComparison.Ordinal);
        Assert.Contains("__SetterDimensionsModel_Width(ref value, p0);", source, StringComparison.Ordinal);
        Assert.Contains("void __SetterDimensionsModel_Width(ref global::Shop.Dimensions target, decimal value);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Accessors_into_a_constrained_generic_base_carry_its_constraints()
    {
        // Without them `Entity<TId>` inside `__Setter0_Name<TId>` is CS0453 for `struct`, CS0452 for `class`,
        // CS0315/CS0311 for a base type or new(), and CS8714 — only a warning — for `notnull`.
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

        Assert.Contains("private static class __Setter0_Name<TId> where TId : struct", run.GeneratedSource("Shop.ProductModel"), StringComparison.Ordinal);
        Assert.Contains("private static class __Setter0_Code<TId> where TId : notnull", run.GeneratedSource("Shop.VoucherModel"), StringComparison.Ordinal);
        Assert.Contains("private static class __Setter0_Label<TKey> where TKey : class", run.GeneratedSource("Shop.SlugModel"), StringComparison.Ordinal);
        Assert.Contains(
            "private static class __Setter0_Rank<TId, TSelf> where TId : unmanaged, global::System.IComparable<TId> where TSelf : global::Shop.Ranked<TId, TSelf>, new()",
            run.GeneratedSource("Shop.LevelModel"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_value_objects_sharing_a_name_get_a_nested_model_each()
    {
        // Keyed by simple name, Billing.Money's model was dropped in favour of Shop.Money's, and Fee was
        // converted into a class that did not have its members.
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
        Assert.Contains("new global::Billing.Money(model.Fee.Cents)", source, StringComparison.Ordinal);
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
        Assert.Contains(
            "public DModel Direct { get; set; } = new();",
            run.GeneratedSource("Shop.OrderModel"),
            StringComparison.Ordinal);
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
    public void Navigations_collections_and_computed_properties_are_left_off_but_backing_fields_are_written()
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
        Assert.Contains("Name = \"<Code>k__BackingField\"", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"<Quantity>k__BackingField\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_internal_entity_gets_internal_members_and_a_global_one_compiles()
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
        Assert.Contains("internal static class NoteModelExtensions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_without_a_parameterless_constructor_gets_no_CreateAsync_and_RASK081()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Model<Guid>
            {
                public Product(string name) => Name = name;
                public string Name { get; private set; }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK081", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Empty(run.GeneratedCompileErrors());

        // Any mention at all — the member, or a doc comment pointing a reader at a member that is not there.
        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.DoesNotContain("CreateAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::System.Guid id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
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
