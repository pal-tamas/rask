using System.Globalization;
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
            public sealed class Product : Aggregate<Guid>
            {
                private Product() { }
                [Required, MaxLength(200)] public string Name { get; private set; } = "";
                public decimal Price { get; set; }
                public string? Notes { get; private set; }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.Contains("public sealed partial class ProductModel", source, StringComparison.Ordinal);
        Assert.Contains("public string? Name { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("[global::System.ComponentModel.DataAnnotations.RequiredAttribute()]", source, StringComparison.Ordinal);
        Assert.Contains("[global::System.ComponentModel.DataAnnotations.MaxLengthAttribute(200)]", source, StringComparison.Ordinal);
        Assert.Contains("public string? Notes { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public int? Version { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public decimal? Price { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public ProductModel() => ProductModelExtensions.__Fill(this, ProductModelExtensions.__Defaults());", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", source, StringComparison.Ordinal);

        Assert.Contains("CreateAsync(global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        Assert.Contains("CreateAsync(global::System.Guid id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        // The creates mirror the updates: a lambda-only form beside each model form.
        Assert.Contains(
            "CreateAsync(global::System.Action<global::Shop.Product> apply, global::Microsoft.EntityFrameworkCore.DbContext? db = null, ",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CreateAsync(global::System.Guid id, global::System.Action<global::Shop.Product> apply, global::Microsoft.EntityFrameworkCore.DbContext? db = null, ",
            source,
            StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::System.Guid id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
        Assert.Contains("DeleteAsync(global::System.Guid id, int? version = null, ", source, StringComparison.Ordinal);
        Assert.Contains("public global::Shop.ProductModel ToModel()", source, StringComparison.Ordinal);
        Assert.Contains(
            "UpdateAsync(global::System.Guid id, global::System.Action<global::Shop.Product> apply, int? version = null, ",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Action<global::Shop.Product>? apply = null, global::Microsoft.EntityFrameworkCore.DbContext? db = null, "
            + "global::System.Threading.CancellationToken cancellationToken = default)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DeleteAsync(global::System.Guid id, int? version = null, global::Microsoft.EntityFrameworkCore.DbContext? db = null, ",
            source,
            StringComparison.Ordinal);
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
            public sealed class Product : Aggregate<Guid>
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
        Assert.Contains(
            ".UpdateAsync<global::Shop.Product>(id!, model.Version, entity => { __Apply(entity, model); apply?.Invoke(entity); }, db, cancellationToken);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("<param name=\"id\">The id of the row to update.</param>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_aggregate_is_versioned_so_a_delete_takes_an_optional_version()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Tag : Aggregate<Guid>
            {
                public string Label { get; private set; } = "";
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "DeleteAsync(global::System.Guid id, int? version = null, global::Microsoft.EntityFrameworkCore.DbContext? db = null, ",
            run.GeneratedSource("Shop.TagModel"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_mapped_property_is_on_the_model_and_a_null_clears_only_a_nullable_one()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Aggregate<Guid>
            {
                public string Name { get; private set; } = "";
                public int Views { get; private set; }
                public string? Notes { get; private set; }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.Contains("public int? Views { get; set; }", source, StringComparison.Ordinal);

        // Notes is nullable on the aggregate, so an emptied form field clears it; Name and Views are not, so a null
        // leaves them as they are.
        Assert.Contains("__Setter2_Notes(entity, null);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__Setter0_Name(entity, null);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__Setter1_Views(entity, null);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_strongly_typed_guid_id_is_the_key_parameter_and_is_assigned_on_create()
    {
        // EF Core does not generate a key behind a value converter, so the create assigns one — only when the
        // constructor left it unset — through a generic accessor into Aggregate<TId>.Id.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly record struct ProductId(Guid Value);
            public sealed class Product : Aggregate<ProductId>
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
            public sealed class Product : Aggregate<ProductId> { private Product() { } }
            public sealed class Tag : Aggregate<Guid> { private Tag() { } }
            public sealed class Note : Aggregate<int> { private Note() { } }
            public sealed class Entry : Aggregate<long> { private Entry() { } }
            public sealed class Order : Aggregate<OrderNumber> { private Order() { } }
            public sealed class Item : Aggregate<Sku> { private Item() { } }
            public sealed class Country : Aggregate<string> { private Country() { } }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        foreach (var (name, id) in new[] { ("Product", "global::Shop.ProductId"), ("Tag", "global::System.Guid") })
        {
            var source = run.GeneratedSource("Shop." + name + "Model");
            Assert.Contains($"CreateAsync(global::Shop.{name}Model model, ", source, StringComparison.Ordinal);
            Assert.Contains($"CreateAsync({id} id, global::Shop.{name}Model model, ", source, StringComparison.Ordinal);
        }

        // A store-generated integer gets no create that takes a key: an explicit identity value fails on SQL Server
        // and leaves PostgreSQL's sequence behind.
        foreach (var (name, id) in new[] { ("Note", "int"), ("Entry", "long") })
        {
            var source = run.GeneratedSource("Shop." + name + "Model");
            Assert.Contains($"CreateAsync(global::Shop.{name}Model model, ", source, StringComparison.Ordinal);
            Assert.DoesNotContain($"CreateAsync({id} id, ", source, StringComparison.Ordinal);
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
        Assert.Contains("__SetterKey_Id<global::System.Guid>.Invoke(entity, id);", run.GeneratedSource("Shop.TagModel"), StringComparison.Ordinal);
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
            public sealed record Money(decimal Amount, string Currency);
            public sealed class Packaging
            {
                public Money Cost { get; set; } = new(0m, "EUR");
                public string Material { get; set; } = "card";
            }
            public sealed class Order : Aggregate<Guid>
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
        Assert.Contains("public MoneyModel? Total { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public PackagingModel? Box { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("new global::Shop.Money((__v0.Amount ?? entity.Total?.Amount ?? default(decimal)!), (__v0.Currency ?? entity.Total?.Currency ?? default(string)!))", source, StringComparison.Ordinal);
        Assert.Contains("new global::Shop.Packaging { Cost = ", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_computed_property_on_a_value_object_neither_joins_its_model_nor_costs_it_the_rebuild()
    {
        // Counting `Display` made the record's primary constructor one parameter short, so no build strategy fit and
        // the value object fell back to a raw copy: `Money Total` on the model, and a null insert from an empty form.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed record Money(decimal Amount, string Currency)
            {
                public string Display => $"{Amount} {Currency}";
            }
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public Money Total { get; private set; } = new(0m, "EUR");
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("public MoneyModel? Total { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("new global::Shop.Money((__v0.Amount ?? entity.Total?.Amount ?? default(decimal)!), (__v0.Currency ?? entity.Total?.Currency ?? default(string)!))", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Display", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_constructor_whose_parameter_types_differ_from_the_properties_is_not_the_one_called()
    {
        // `string currency` parsed into `CurrencyCode Currency`: passing the property straight in would be CS1503
        // inside generated code. The public setters are the rebuild instead.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public enum CurrencyCode { Eur, Huf }
            public sealed class Money
            {
                public Money() { }
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = Enum.Parse<CurrencyCode>(currency, ignoreCase: true);
                }
                public decimal Amount { get; set; }
                public CurrencyCode Currency { get; set; }
            }
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public Money Total { get; private set; } = new();
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("new global::Shop.Money { Amount = (__v0.Amount ?? entity.Total?.Amount ?? default(decimal)!), Currency = (__v0.Currency ?? entity.Total?.Currency ?? default(global::Shop.CurrencyCode)!) }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_the_store_generates_gets_no_create_that_takes_one()
    {
        var run = Run("""
            using Rask.Data;
            namespace Shop;
            public sealed class Coupon : Aggregate<int>
            {
                private Coupon() { }
                public string Code { get; private set; } = "";
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.CouponModel");
        Assert.Contains("CreateAsync(global::Shop.CouponModel model, ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync(int id, ", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(int id, global::Shop.CouponModel model, ", source, StringComparison.Ordinal);
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
            public sealed class Address
            {
                private Address() { }
                public string Street { get; private set; } = "";
                public string? City { get; private set; }
            }
            public sealed class Weight
            {
                private Weight(decimal amount, string unit) { Amount = amount; Unit = unit; }
                public decimal Amount { get; }
                public string Unit { get; }
                public static Weight Of(decimal amount, string unit) => new(amount, unit);
            }
            public struct Dimensions
            {
                public decimal Width { get; private set; }
                public decimal Height { get; private set; }
            }
            public sealed class Shipment : Aggregate<Guid>
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
        Assert.Contains("public string? Street { get; set; }", source, StringComparison.Ordinal);

        // Private parameterless constructor, then private setters.
        Assert.Contains("private static extern global::Shop.Address __NewAddressModel();", source, StringComparison.Ordinal);
        Assert.Contains("private static global::Shop.Address __BuildAddressModel(string p0, string? p1)", source, StringComparison.Ordinal);
        Assert.Contains("__SetterAddressModel_Street(value, p0);", source, StringComparison.Ordinal);
        Assert.Contains("__BuildAddressModel((__v0.Street ?? entity.Destination?.Street ?? default(string)!), (__v0.City ?? entity.Destination?.City))", source, StringComparison.Ordinal);
        Assert.Contains("__BuildAddressModel((__v1.Street ?? entity.ReturnTo?.Street ?? default(string)!), (__v1.City ?? entity.ReturnTo?.City))", source, StringComparison.Ordinal);

        // Private constructor naming every property.
        Assert.Contains("private static extern global::Shop.Weight __NewWeightModel(decimal p0, string p1);", source, StringComparison.Ordinal);
        Assert.Contains("__NewWeightModel((__v2.Amount ?? entity.Weight?.Amount ?? default(decimal)!), (__v2.Unit ?? entity.Weight?.Unit ?? default(string)!))", source, StringComparison.Ordinal);

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
            public abstract class ValueKeyed<TId> : Aggregate<TId> where TId : struct
            {
                public string Name { get; private set; } = "";
            }
            public abstract class NotNullKeyed<TId> : Aggregate<TId> where TId : notnull
            {
                public string Code { get; private set; } = "";
            }
            public abstract class ReferenceKeyed<TKey> : Aggregate<TKey> where TKey : class
            {
                public string Label { get; private set; } = "";
            }
            public abstract class Ranked<TId, TSelf> : Aggregate<TId>
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
    public void A_one_value_value_object_is_carried_as_its_value()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed record Email(string Value);
            public sealed class Customer : Aggregate<Guid>
            {
                public Email Email { get; private set; } = new("");
                public Email? Backup { get; private set; }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.CustomerModel");
        Assert.Contains("public string? Email { get; set; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EmailModel", source, StringComparison.Ordinal);
        Assert.Contains("model.Email = entity.Email?.Value;", source, StringComparison.Ordinal);
        Assert.Contains("new global::Shop.Email(__v0)", source, StringComparison.Ordinal);
        Assert.Contains("__Setter1_Backup(entity, null);", source, StringComparison.Ordinal);
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
                public sealed record Money(long Cents, string Unit);
            }
            namespace Shop
            {
                public sealed record Money(decimal Amount, string Currency);
                public sealed class Order : Aggregate<Guid>
                {
                    private Order() { }
                    public Money Total { get; private set; } = new(0m, "EUR");
                    public Billing.Money Fee { get; private set; } = new(0, "cent");
                }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("public MoneyModel? Total { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("public MoneyModel2? Fee { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("new global::Billing.Money((__v1.Cents ?? entity.Fee?.Cents ?? default(long)!), (__v1.Unit ?? entity.Fee?.Unit ?? default(string)!))", source, StringComparison.Ordinal);
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
            public sealed record Leaf(string Code);
            public sealed record D(Leaf Leaf);
            public sealed record C(D D);
            public sealed record B(C C);
            public sealed record A(B B);
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public A Deep { get; private set; } = null!;
                public D Direct { get; private set; } = null!;
            }
            """);

        // Compiling is the assertion: every use of D agrees with the one shape emitted for it.
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" Direct { get; set; }", run.GeneratedSource("Shop.OrderModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void An_attribute_taking_an_array_is_copied_as_one()
    {
        var run = Run("""
            using System;
            using System.ComponentModel.DataAnnotations;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Aggregate<Guid>
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
            public sealed class Customer : Aggregate<Guid>
            {
                private Customer() { }
            }
            public sealed class Order : Aggregate<Guid>
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
        Assert.Contains("public global::System.Guid? CustomerId { get; set; }", source, StringComparison.Ordinal);
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
            internal sealed class Note : Aggregate<int>
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
    public void An_entity_without_a_parameterless_constructor_gets_no_CreateAsync_and_RASK086()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Aggregate<Guid>
            {
                public Product(string name) => Name = name;
                public string Name { get; private set; }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK086", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.EndsWith("/docs/diagnostics.md#rask086", diagnostic.Descriptor.HelpLinkUri, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());

        // Any mention at all — the member, or a doc comment pointing a reader at a member that is not there.
        var source = run.GeneratedSource("Shop.ProductModel");
        Assert.DoesNotContain("CreateAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::System.Guid id, global::Shop.ProductModel model, ", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregates_children_are_carried_on_its_model_as_an_editable_list()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<OrderLine> _lines = [];
                private Order() { }
                public string Reference { get; private set; } = "";
                public IReadOnlyCollection<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }
                public string Product { get; private set; } = "";
                public int Quantity { get; private set; }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var order = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains(
            "public global::System.Collections.Generic.List<global::Shop.OrderLineModel> Lines { get; set; } = [];",
            order,
            StringComparison.Ordinal);

        // The child's own model exists, carries an id so a save can match rows, and has no writes of its own:
        // it is created, changed and removed as part of the order.
        var line = run.GeneratedSource("Shop.OrderLineModel");
        Assert.Contains("public global::System.Guid? Id { get; set; }", line, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync", line, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateAsync", line, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync", line, StringComparison.Ordinal);

        // The read-only view is written through its backing field.
        Assert.Contains("Name = \"_lines\"", order, StringComparison.Ordinal);
    }

    [Fact]
    public void A_writable_collection_is_synced_through_the_property_itself()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public List<OrderLine> Lines { get; private set; } = [];
            }
            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }
                public int Quantity { get; private set; }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var order = run.GeneratedSource("Shop.OrderModel");
        Assert.Contains("var __children = entity.Lines;", order, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsafeAccessorKind.Field", order, StringComparison.Ordinal);
    }

    // ---- RaskReadFacesOnly: a package that maps its own tables ---------------------------------------

    private static GeneratorRun RunAll(string source, IReadOnlyDictionary<string, string>? options) =>
        GeneratorHarness.Run(
            source,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            options,
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

    [Fact]
    public void RaskReadFacesOnly_emits_the_read_face_and_nothing_else()
    {
        var run = RunAll(
            """
            using System;
            using Rask.Data;
            namespace Rask.Auth;
            public sealed class Session : Aggregate<Guid>
            {
                public Guid UserId { get; private set; }
            }
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.RaskReadFacesOnly"] = "true",
            });

        // What a package querying somebody else's table needs.
        Assert.True(run.HasGeneratedSource("SessionRead"));
        Assert.True(run.HasGeneratedSource("__RaskReadModelRegistry"));

        // NOT the registry: it is applied from a [ModuleInitializer], so a contribution here would map this
        // table into every app that references the package — auth switched off included — and a second time
        // for one that maps it by hand.
        Assert.False(run.HasGeneratedSource("__RaskModelRegistry"));
        Assert.False(run.HasGeneratedSource("__RaskDbSets"));

        // And not the form surface: nothing should create one of these from a posted form.
        Assert.False(run.HasGeneratedSource("SessionModel"));
    }

    [Fact]
    public void Without_the_property_everything_is_generated_as_before()
    {
        var run = RunAll(
            """
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                public string Reference { get; private set; } = "";
            }
            """,
            options: null);

        Assert.True(run.HasGeneratedSource("OrderRead"));
        Assert.True(run.HasGeneratedSource("__RaskModelRegistry"));
        Assert.True(run.HasGeneratedSource("__RaskDbSets"));
        Assert.True(run.HasGeneratedSource("OrderModel"));
    }

    // ---- ModelWrites: narrowing the FORM surface ----------------------------------------------------

    [Fact]
    public void The_generators_copy_of_ModelWrites_still_matches_the_enum()
    {
        // The generator targets netstandard2.0 and never loads Rask.Data, so it restates these values. A
        // member reordered or renumbered on the enum would otherwise silently narrow the wrong surface.
        Assert.Equal(0, (int)global::Rask.Data.ModelWrites.None);
        Assert.Equal(1, (int)global::Rask.Data.ModelWrites.Create);
        Assert.Equal(2, (int)global::Rask.Data.ModelWrites.Update);
        Assert.Equal(ModelInputGenerator.AllWrites, (int)global::Rask.Data.ModelWrites.All);
    }

    [Fact]
    public void ModelWrites_None_drops_the_model_and_every_write_that_takes_one()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Passkey : Aggregate<Guid>
            {
                public const ModelWrites Writes = ModelWrites.None;
                public string Name { get; private set; } = "";
                public void Rename(string name) => Name = name;
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.PasskeyModel");

        // No model type, and nothing that takes one.
        Assert.DoesNotContain("partial class PasskeyModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToModel()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__Apply", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__Fill", source, StringComparison.Ordinal);

        // The BEHAVIOUR writes are untouched — none of them ever took a model.
        Assert.Contains("CreateAsync(global::System.Action<global::Shop.Passkey> apply", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::System.Guid id, global::System.Action<global::Shop.Passkey> apply", source, StringComparison.Ordinal);
        Assert.Contains("DeleteAsync(global::System.Guid id", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelWrites_None_keeps_the_read_face()
    {
        // The read face is not negotiable: querying works through read models, so an aggregate able to switch
        // its own off would be one that nothing can read.
        var run = GeneratorHarness.Run(
            """
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Passkey : Aggregate<Guid>
            {
                public const ModelWrites Writes = ModelWrites.None;
                public string Name { get; private set; } = "";
            }
            """,
            new ReadModelGenerator(),
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

        Assert.Contains("class PasskeyRead", run.GeneratedSource("Shop_PasskeyRead"), StringComparison.Ordinal);
    }

    [Fact]
    public void ModelWrites_Update_keeps_the_model_and_drops_only_the_create_that_takes_it()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Invoice : Aggregate<Guid>
            {
                public const ModelWrites Writes = ModelWrites.Update;
                public string Title { get; private set; } = "";
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("Shop.InvoiceModel");

        Assert.Contains("partial class InvoiceModel", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::System.Guid id, global::Shop.InvoiceModel model", source, StringComparison.Ordinal);
        Assert.Contains("ModelAsync", source, StringComparison.Ordinal);

        // A row a form may edit but never make.
        Assert.DoesNotContain("CreateAsync(global::Shop.InvoiceModel model", source, StringComparison.Ordinal);
        Assert.Contains("CreateAsync(global::System.Action<global::Shop.Invoice> apply", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_Writes_const_generates_everything_it_always_did()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Invoice : Aggregate<Guid>
            {
                public string Title { get; private set; } = "";
            }
            """);

        Assert.Empty(run.Diagnostics);
        var source = run.GeneratedSource("Shop.InvoiceModel");

        Assert.Contains("partial class InvoiceModel", source, StringComparison.Ordinal);
        Assert.Contains("CreateAsync(global::Shop.InvoiceModel model", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(global::System.Guid id, global::Shop.InvoiceModel model", source, StringComparison.Ordinal);
        Assert.Contains("ModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("ToModel()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_child_declaring_its_own_Writes_is_RASK091_and_is_ignored()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<OrderLine> _lines = [];
                public IReadOnlyCollection<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid>
            {
                public const ModelWrites Writes = ModelWrites.None;
                public int Quantity { get; private set; }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK091", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);

        // Ignored, not obeyed: the root's model holds a list of the child model, so honouring this would
        // leave the root pointing at a type that was never generated.
        Assert.Contains("partial class OrderLineModel", run.GeneratedSource("Shop.OrderLineModel"), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_child_of_a_root_with_no_form_gets_no_model_either()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                public const ModelWrites Writes = ModelWrites.None;
                private readonly List<OrderLine> _lines = [];
                public IReadOnlyCollection<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid>
            {
                public int Quantity { get; private set; }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        // A child model exists to be an element of the root's list. With no root form there is nothing to
        // be an element of, and a child has no writes of its own that could want one.
        Assert.DoesNotContain("partial class OrderModel", run.GeneratedSource("Shop.OrderModel"), StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OrderLineModel", run.GeneratedSource("Shop.OrderLineModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_collection_of_aggregates_is_RASK087_and_is_not_a_child()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<Shipment> _shipments = [];
                private Order() { }
                public IReadOnlyCollection<Shipment> Shipments => _shipments;
            }
            public sealed class Shipment : Aggregate<Guid>
            {
                private Shipment() { }
                public string Carrier { get; private set; } = "";
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK087", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.EndsWith("/docs/diagnostics.md#rask087", diagnostic.Descriptor.HelpLinkUri, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());

        // A collection is fixed on the OTHER side — the shipment holds the order's id, not the reverse.
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("let each 'Shipment' hold Order's id", message, StringComparison.Ordinal);
        Assert.Contains("Shipment.Read", message, StringComparison.Ordinal);

        // Another aggregate is somebody else's data: nothing about it reaches this model, so a form post on the
        // order can neither add a shipment nor delete one.
        var order = run.GeneratedSource("Shop.OrderModel");
        Assert.DoesNotContain("Shipments", order, StringComparison.Ordinal);

        // And the shipment keeps its own writes, because it is a root.
        Assert.Contains("CreateAsync", run.GeneratedSource("Shop.ShipmentModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_to_one_aggregate_is_RASK087_and_names_the_id_that_replaces_it()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public Customer Customer { get; private set; } = null!;
            }
            public sealed class Customer : Aggregate<Guid>
            {
                private Customer() { }
                public string Name { get; private set; } = "";
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK087", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // The fix is the id on THIS side, spelled with the target's own key type, and the reader is told
        // where the join went rather than being left to wonder.
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'Guid CustomerId'", message, StringComparison.Ordinal);
        Assert.Contains("Order.Read", message, StringComparison.Ordinal);
        Assert.Contains("inferred back as 'Customer'", message, StringComparison.Ordinal);

        // Nothing about the customer reaches the order's form model.
        Assert.DoesNotContain("Customer", run.GeneratedSource("Shop.OrderModel"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_to_a_CHILD_entity_is_not_RASK087()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public OrderNote Note { get; private set; } = null!;
            }
            public sealed class OrderNote : Entity<Guid>
            {
                private OrderNote() { }
                public string Text { get; private set; } = "";
            }
            """);

        // The border is between AGGREGATES. A part of this one is a part, and holding it is the point.
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK087");
    }

    [Fact]
    public void A_child_collection_with_nothing_to_write_is_RASK088()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<OrderLine> _paid = [];
                private readonly List<OrderLine> _unpaid = [];
                private Order() { }
                public IEnumerable<OrderLine> Lines => _paid.Concat(_unpaid);
            }
            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }
                public int Quantity { get; private set; }
            }
            """);

        // Two candidate fields and a property that is not a collection to write: Rask says so rather than
        // guessing which field the property hands out.
        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK088", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.EndsWith("/docs/diagnostics.md#rask088", diagnostic.Descriptor.HelpLinkUri, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_hand_written_model_of_the_same_name_is_RASK082_and_nothing_is_generated()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Aggregate<Guid>
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
            public sealed class Product : Aggregate<Guid>
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
                public sealed class Product : Aggregate<Guid>
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
