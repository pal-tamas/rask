namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// The rules <see cref="ModelInputGenerator"/> announces for a form model: a one-value value object's own
/// <c>Validate</c>, and the unique rules the aggregate declares, asked of the store.
/// </summary>
public class ModelRulesGeneratorTests
{
    private const string Shop = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Shop;
        public sealed record Sku(string Value)
        {
            public static IEnumerable<string> Validate(string value) => value.Length < 3 ? ["Too short."] : [];
        }
        public readonly record struct Stock(int Value)
        {
            public static ValueTask<IEnumerable<string>> Validate(int value) => new(value < 0 ? ["Negative."] : []);
        }
        public sealed record Barcode(string Value)
        {
            public static IEnumerable<string> Validate(int value) => [];
            public static string Validate(string value) => "";
            internal static IEnumerable<string> Validate(string value, bool strict) => [];
        }
        public sealed record Money(decimal Amount, string Currency)
        {
            public static IEnumerable<string> Validate(Money value) => [];
        }
        public sealed record Size(Sku Code, int Units);
        """;

    // The harness compiles against everything the test project references, forms included.
    private static GeneratorRun RunWithForms(string source) =>
        GeneratorHarness.Run(
            Shop + source, new ModelInputGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    private static GeneratorRun RunWithoutForms(string source) =>
        GeneratorHarness.RunWithout(
            Shop + source, new ModelInputGenerator(), ["Rask.Core"], "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    private const string Product = """
        public sealed class Product : Aggregate<Guid>
        {
            private Product() { }
            public Sku Sku { get; private set; } = new("");
            public Stock Stock { get; private set; }
            public Barcode Barcode { get; private set; } = new("");
            public Money Price { get; private set; } = new(0, "EUR");
            public Size Size { get; private set; } = new(new(""), 0);
            public string Title { get; private set; } = "";
            public static void Configure(EntityTypeBuilder<Product> builder) =>
                builder.HasIndex(p => p.Sku).IsUnique("This SKU is taken.");
        }
        """;

    [Fact]
    public void A_one_value_value_objects_Validate_becomes_one_kept_rule_of_the_fields_own_type()
    {
        var run = RunWithForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "global::Rask.Core.Forms.RaskValidation.RegisterFieldRules(typeof(global::Shop.ProductModel), Root_RuleOf);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly global::Rask.Core.Forms.Validate<string?> Root_Sku =\n        static value => value is { } held ? global::Shop.Sku.Validate(held) : global::System.Array.Empty<string>();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly global::System.Func<int?, global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<string>>> Root_Stock =\n        static async value => value is { } held ? await global::Shop.Stock.Validate(held) : global::System.Array.Empty<string>();",
            source,
            StringComparison.Ordinal);
        Assert.Contains("\"Sku\" => Root_Sku,\n            \"Stock\" => Root_Stock,\n            _ => null,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Validate_of_any_other_shape_is_not_a_rule_and_neither_is_one_on_a_value_object_of_several_values()
    {
        var run = RunWithForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel");

        Assert.DoesNotContain("Root_Barcode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Root_Price", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Root_Title", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_object_held_inside_another_is_ruled_on_the_nested_model_that_carries_its_field()
    {
        var run = RunWithForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel").ReplaceLineEndings("\n");

        Assert.Contains(
            "RegisterFieldRules(typeof(global::Shop.ProductModel.SizeModel), SizeModel_RuleOf);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("\"Code\" => SizeModel_Code,\n            _ => null,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_that_declares_a_unique_rule_with_a_message_announces_its_store_rules_for_the_root_model()
    {
        var run = RunWithForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "global::Rask.Core.Forms.RaskValidation.RegisterStoreRules(typeof(global::Shop.ProductModel), StoreRules);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Data.GeneratedStoreRules.Check<global::Shop.Product, global::Shop.ProductModel>(services, typed, field, ValueOf, KeyOf, cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Sku\" => model.Sku,\n            \"Stock\" => model.Stock,\n            \"Barcode\" => model.Barcode,\n            \"Title\" => model.Title,\n            _ => null,",
            source,
            StringComparison.Ordinal);
        Assert.Contains("private static object? KeyOf(global::Shop.ProductModel model) => model.__Key;", source, StringComparison.Ordinal);
        Assert.Contains("internal global::System.Guid? __Key { get; set; }", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("builder.HasIndex(p => p.Title).IsUnique();")]
    [InlineData("builder.HasIndex(p => p.Title).IsUnique(true);")]
    [InlineData("builder.HasIndex(p => p.Title);")]
    [InlineData("builder.ToTable(\"Notes\");")]
    public void An_aggregate_with_no_unique_rule_that_carries_a_message_gives_its_forms_nothing_to_wait_for(string configure)
    {
        var run = RunWithForms($$"""
            public sealed class Note : Aggregate<Guid>
            {
                private Note() { }
                public string Title { get; private set; } = "";
                public static void Configure(EntityTypeBuilder<Note> builder) { {{configure}} }
            }
            """);

        var source = run.GeneratedSource("Shop.NoteModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("RegisterStoreRules", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__NoteModelRules", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_inside_an_aggregate_gets_its_value_objects_rules_and_no_store_rules_or_key_of_its_own()
    {
        var run = RunWithForms("""
            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<OrderLine> _lines = [];
                private Order() { }
                public IReadOnlyList<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }
                public Sku Sku { get; private set; } = new("");
                public static void Configure(EntityTypeBuilder<OrderLine> builder) =>
                    builder.HasIndex(l => l.Sku).IsUnique("Twice on one order.");
            }
            """);

        var line = run.GeneratedSource("Shop.OrderLineModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("RegisterFieldRules(typeof(global::Shop.OrderLineModel), Root_RuleOf);", line, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterStoreRules", line, StringComparison.Ordinal);
        Assert.DoesNotContain("__Key", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_that_models_its_data_and_references_no_forms_is_generated_without_either()
    {
        var run = RunWithoutForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("RaskValidation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__ProductModelRules", source, StringComparison.Ordinal);
    }
}
