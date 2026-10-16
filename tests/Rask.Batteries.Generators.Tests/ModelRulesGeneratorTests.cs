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

    private const string Required = "global::Rask.Data.GeneratedModelRules.Required(__RequiredText())";

    private const string Product = """
        public sealed class Product : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
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
            "private static readonly global::Rask.Core.Forms.Validate<string?> Root_Sku =\n        global::Shop.ProductModelExtensions.__Rule_Root_Sku;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly global::System.Func<int?, global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<string>>> Root_Stock =\n        global::Shop.ProductModelExtensions.__Rule_Root_Stock;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("\"Sku\" => Root_Sku,\n            \"Stock\" => Root_Stock,\n            \"Barcode\" => Root_Barcode,\n            \"Title\" => Root_Title,\n            _ => null,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_that_cannot_be_null_asks_its_value_object_about_the_empty_value_and_carries_no_attribute()
    {
        var run = RunWithForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel").ReplaceLineEndings("\n");

        // Text has an empty value the type can be asked about; a number has none, so the one sentence stands in.
        Assert.Contains(
            "internal static global::System.Collections.Generic.IEnumerable<string> __Rule_Root_Sku(string? value) =>\n        value is { } held ? global::Shop.Sku.Validate(held) : global::Rask.Data.GeneratedModelRules.OrRequired(global::Shop.Sku.Validate(\"\"), __RequiredText());",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal static async global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<string>> __Rule_Root_Stock(int? value) =>\n        value is { } held ? await global::Shop.Stock.Validate(held) : global::Rask.Data.GeneratedModelRules.Required(__RequiredText());",
            source,
            StringComparison.Ordinal);
        Assert.Contains("\n    public string? Sku { get; set; }", source.Replace("    /// <summary>The form value of <see cref=\"global::Shop.Product.Sku\" />.</summary>\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("RequiredAttribute()]\n    public string? Sku", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiredAttribute()]\n    public int? Stock", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_value_object_property_is_not_asked_about_nothing_and_one_with_no_rule_only_has_to_be_filled()
    {
        var run = RunWithForms("""
            public sealed class Item : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Item() { }
                public Sku? Sku { get; private set; }
                public Money Price { get; private set; } = new(0, "EUR");
                public Barcode Barcode { get; private set; } = new("");
            }
            """);

        var source = run.GeneratedSource("Shop.ItemModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "__Rule_Root_Sku(string? value) =>\n        value is { } held ? global::Shop.Sku.Validate(held) : global::System.Array.Empty<string>();",
            source,
            StringComparison.Ordinal);

        // No rule of its own, so only that it is filled: the framework's sentence, and still no attribute.
        Assert.Contains(
            "__Rule_Root_Barcode(string? value) =>\n        string.IsNullOrWhiteSpace(value) ? global::Rask.Data.GeneratedModelRules.Required(__RequiredText()) : global::System.Array.Empty<string>();",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("RequiredAttribute", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_asks_every_rule_the_form_asks_and_refuses_before_it_touches_the_database()
    {
        var run = RunWithForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel").ReplaceLineEndings("\n");

        // Stock's rule is awaited, so the writes are; the rest is asked first, and in one place.
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Equal(3, source.Split("await __ValidateAwaited(model).ConfigureAwait(false);").Length - 1);
        Assert.Contains(
            "failures = global::Rask.Data.GeneratedModelRules.Told(failures, path, \"Sku\", __Rule_Root_Sku(model.Sku));",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (model.Size is { } __held1)\n        {\n            failures = global::Rask.Data.GeneratedModelRules.Told(failures, path, \"Size.Code\", __Rule_SizeModel_Code(__held1.Code));\n" +
            "            failures = global::Rask.Data.GeneratedModelRules.Told(failures, path, \"Size.Units\", __Rule_SizeModel_Units(__held1.Units));\n        }\n" +
            "        else\n        {\n            failures = global::Rask.Data.GeneratedModelRules.Told(failures, path, \"Size\", global::Rask.Data.GeneratedModelRules.Required(__RequiredText()));\n        }",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "failures = global::Rask.Data.GeneratedModelRules.Told(failures, path, \"Stock\", await __Rule_Root_Stock(model.Stock).ConfigureAwait(false));",
            source,
            StringComparison.Ordinal);
        Assert.Contains("throw global::Rask.Data.GeneratedModelRules.Refused(failures);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidationException(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_property_that_cannot_be_null_is_required_by_a_generated_rule_with_the_catalogs_sentence()
    {
        var run = RunWithForms("""
            public sealed class Trailer : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Trailer() { }
                public int Weight { get; private set; }
                public string Plate { get; private set; } = "";
                public DateOnly Built { get; private set; }
                public bool Braked { get; private set; }
                public int? Axles { get; private set; }
                public string? Notes { get; private set; }
                [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Say who owns it.")]
                public string Owner { get; private set; } = "";
            }
            """);

        var source = run.GeneratedSource("Shop.TrailerModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("__Rule_Root_Weight(int? value) =>\n        value is null ? REQ : global::System.Array.Empty<string>();".Replace("REQ", Required, StringComparison.Ordinal), source, StringComparison.Ordinal);
        Assert.Contains("__Rule_Root_Plate(string? value) =>\n        string.IsNullOrWhiteSpace(value) ? REQ : global::System.Array.Empty<string>();".Replace("REQ", Required, StringComparison.Ordinal), source, StringComparison.Ordinal);
        Assert.Contains("__Rule_Root_Built(global::System.DateOnly? value) =>\n        value is null ? ", source, StringComparison.Ordinal);
        Assert.Contains("__Rule_Root_Braked(bool? value) =>\n        value is null ? ", source, StringComparison.Ordinal);
        Assert.Contains(
            "private static string __RequiredText() =>\n        global::Rask.Core.Globalization.RaskStrings.Get(global::Rask.Core.Globalization.RaskString.ValidationRequired, global::Rask.Data.GeneratedModelRules.RequiredMessage);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("private static readonly global::Rask.Core.Forms.Validate<int?> Root_Weight =\n        global::Shop.TrailerModelExtensions.__Rule_Root_Weight;", source, StringComparison.Ordinal);

        // Optional stays optional, and what the app wrote itself stays the app's: its attribute, its message.
        Assert.DoesNotContain("__Rule_Root_Axles", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__Rule_Root_Notes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__Rule_Root_Owner", source, StringComparison.Ordinal);
        Assert.Contains("RequiredAttribute() { ErrorMessage = \"Say who owns it.\" }", source, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("RequiredAttribute").Length - 1);
    }

    [Fact]
    public void Each_generated_model_type_announces_exactly_the_fields_its_write_refuses_unfilled()
    {
        var run = RunWithForms("""
            public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private readonly List<OrderLine> _lines = [];
                private Order() { }
                public Sku Sku { get; private set; } = new("");
                public Sku? Alias { get; private set; }
                public int Weight { get; private set; }
                public int? Axles { get; private set; }
                public Size Size { get; private set; } = new(new(""), 0);
                [System.ComponentModel.DataAnnotations.Required] public string Owner { get; private set; } = "";
                public IReadOnlyList<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }
                public Sku Sku { get; private set; } = new("");
                public string? Note { get; private set; }
            }
            """);

        var order = run.GeneratedSource("Shop.OrderModel").ReplaceLineEndings("\n");
        var line = run.GeneratedSource("Shop.OrderLineModel").ReplaceLineEndings("\n");

        // The root, the value object it holds and the row each answer for themselves, by simple name. Optional
        // members are not listed, and neither is the one the app marked itself: its attribute answers.
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("RaskValidation.RegisterRequiredFields(typeof(global::Shop.OrderModel), Root_IsRequired);", order, StringComparison.Ordinal);
        Assert.Contains("private static bool Root_IsRequired(string property) =>\n        property is \"Sku\" or \"Weight\";", order, StringComparison.Ordinal);
        Assert.Contains("RaskValidation.RegisterRequiredFields(typeof(global::Shop.OrderModel.SizeModel), SizeModel_IsRequired);", order, StringComparison.Ordinal);
        Assert.Contains("private static bool SizeModel_IsRequired(string property) =>\n        property is \"Code\" or \"Units\";", order, StringComparison.Ordinal);
        Assert.Contains("RaskValidation.RegisterRequiredFields(typeof(global::Shop.OrderLineModel), Root_IsRequired);", line, StringComparison.Ordinal);
        Assert.Contains("private static bool Root_IsRequired(string property) =>\n        property is \"Sku\";", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_model_whose_rules_refuse_nothing_unfilled_announces_no_required_field()
    {
        var run = RunWithForms("""
            public sealed class Item : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Item() { }
                public Sku? Sku { get; private set; }
            }
            """);

        var source = run.GeneratedSource("Shop.ItemModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("RegisterFieldRules(typeof(global::Shop.ItemModel), Root_RuleOf);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterRequiredFields", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsRequired", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_strings_catalog_the_one_sentence_is_the_english_constant()
    {
        var run = RunWithoutForms("""
            public sealed class Trailer : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Trailer() { }
                public int Weight { get; private set; }
            }
            """);

        var source = run.GeneratedSource("Shop.TrailerModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("private static string __RequiredText() =>\n        global::Rask.Data.GeneratedModelRules.RequiredMessage;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RaskStrings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterRequiredFields", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_model_with_no_rule_and_no_attribute_is_asked_nothing_and_builds_nothing()
    {
        var run = RunWithForms("""
            public sealed class Note : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Note() { }
                public string? Title { get; private set; }
                public int? Pages { get; private set; }
            }
            """);

        var source = run.GeneratedSource("Shop.NoteModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("internal static void __Validate(global::Shop.NoteModel model)\n    {\n    }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__Check", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__ValidateAwaited", source, StringComparison.Ordinal);
        Assert.Contains("__Collect(global::Shop.NoteModel model, string path, global::System.Collections.Generic.IList<global::Rask.Wire.FieldFailure>? failures)\n    {\n        return failures;\n    }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_asks_its_rows_under_the_path_a_form_names_them_by_and_only_the_rows_that_have_something_to_ask()
    {
        var run = RunWithForms("""
            public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private readonly List<OrderLine> _lines = [];
                private readonly List<OrderNote> _notes = [];
                private Order() { }
                public IReadOnlyList<OrderLine> Lines => _lines;
                public IReadOnlyList<OrderNote> Notes => _notes;
            }
            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }
                public Sku Sku { get; private set; } = new("");
            }
            public sealed class OrderNote : Entity<Guid>
            {
                private OrderNote() { }
                public string? Text { get; private set; }
            }
            """);

        var order = run.GeneratedSource("Shop.OrderModel").ReplaceLineEndings("\n");
        var line = run.GeneratedSource("Shop.OrderLineModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "failures = global::Shop.OrderLineModelExtensions.__Collect(row, path + \"Lines[\" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"].\", failures);",
            order,
            StringComparison.Ordinal);
        Assert.DoesNotContain("OrderNoteModelExtensions.__Collect", order, StringComparison.Ordinal);
        Assert.Contains("Told(failures, path, \"Sku\", __Rule_Root_Sku(model.Sku));", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_rule_with_a_nullable_parameter_is_asked_with_the_empty_string_and_never_null()
    {
        var run = RunWithForms("""
            public sealed record Label(string Value)
            {
                public static IEnumerable<string> Validate(string? value) => string.IsNullOrEmpty(value) ? ["Name it."] : [];
            }
            public sealed class Shelf : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Shelf() { }
                public Label Label { get; private set; } = new("");
                public Label? Note { get; private set; }
            }
            """);

        var source = run.GeneratedSource("Shop.ShelfModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "__Rule_Root_Label(string? value) =>\n        value is { } held ? global::Shop.Label.Validate(held) : global::Rask.Data.GeneratedModelRules.OrRequired(global::Shop.Label.Validate(\"\"), __RequiredText());",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "__Rule_Root_Note(string? value) =>\n        value is { } held ? global::Shop.Label.Validate(held) : global::System.Array.Empty<string>();",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Label.Validate(null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Label.Validate(value)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_value_objects_side_by_side_that_each_hold_a_ruled_one_are_asked_under_names_that_do_not_collide()
    {
        var run = RunWithForms("""
            public sealed record Box(Sku Code, int Units);
            public sealed record Crate(Size Size, Sku Code);
            public sealed class Pallet : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Pallet() { }
                public Size Size { get; private set; } = new(new(""), 0);
                public Box Box { get; private set; } = new(new(""), 0);
                public Crate Crate { get; private set; } = new(new(new(""), 0), new(""));
            }
            """);

        var source = run.GeneratedSource("Shop.PalletModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("if (model.Size is { } __held", source, StringComparison.Ordinal);
        Assert.Contains("Told(failures, path, \"Crate.Size.Code\",", source, StringComparison.Ordinal);
        Assert.Contains("Told(failures, path, \"Box.Code\",", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Validate_of_any_other_shape_is_not_a_rule_and_neither_is_one_on_a_value_object_of_several_values()
    {
        var run = RunWithForms(Product);

        var source = run.GeneratedSource("Shop.ProductModel");

        // Barcode's Validate overloads are not rules, so the field only has to be filled; Money holds two values.
        Assert.DoesNotContain("Barcode.Validate(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Root_Price", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Money.Validate(", source, StringComparison.Ordinal);
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
        Assert.Contains("\"Code\" => SizeModel_Code,\n            \"Units\" => SizeModel_Units,\n            _ => null,", source, StringComparison.Ordinal);
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
            public sealed class Note : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
            {
                private Note() { }
                public string? Title { get; private set; }
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
            public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
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
