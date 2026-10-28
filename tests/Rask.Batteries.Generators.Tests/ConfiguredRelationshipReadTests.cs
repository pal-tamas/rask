using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A relationship the entity's own <c>Configure</c> declares decides its read navigation: the target is the
/// <c>HasOne&lt;T&gt;()</c>, and the id's name is only a guess for an id nothing was configured on.
/// </summary>
public class ConfiguredRelationshipReadTests
{
    private const string Excise = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Excise
        {
            public sealed class ProductCategory : Aggregate<int>
            {
                private ProductCategory() { }
                public string Name { get; private set; } = "";
            }
            public sealed class Product : Aggregate<int, PerTenant>
            {
                private Product() { }
                public int? TenantId { get; private set; }
                public int? ProductCategoryId { get; private set; }
                public int? ForeignTaxProductCategoryId { get; private set; }
                public static void Configure(EntityTypeBuilder<Product> b) =>
                    b.HasOne<ForeignTax.ForeignTaxCategory>().WithMany().HasForeignKey(p => p.ForeignTaxProductCategoryId);
            }
        }
        namespace Excise.ForeignTax
        {
            public sealed class ForeignTaxCategory : Aggregate<int>
            {
                private ForeignTaxCategory() { }
                public string Name { get; private set; } = "";
            }
        }
        """;

    private const string Providers = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Excise;
        public sealed class Country : Aggregate<int>
        {
            private Country() { }
        }
        public sealed class Provider : Aggregate<int, PerTenant>
        {
            private Provider() { }
            public int? TenantId { get; private set; }
        }
        public sealed partial class ProviderCountry : Entity<(int TransactionTypeId, Country)>
        {
            private ProviderCountry() { }
            public int TransactionTypeId { get; private set; }
            public string VatNumber { get; private set; } = "";
            public static void Configure(EntityTypeBuilder<ProviderCountry> b) =>
                b.HasOne<Provider>().WithMany().HasForeignKey(c => c.TransactionTypeId);
        }
        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ReadModelGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_configured_target_wins_over_the_aggregate_the_ids_name_ends_in()
    {
        var run = Run(Excise);

        var face = run.GeneratedSource("Excise_ProductRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::Excise.ForeignTax.ForeignTaxCategoryRead? ForeignTaxProductCategory { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("\"ForeignTaxProductCategory\", typeof(global::Excise.ForeignTax.ForeignTaxCategoryRead), \"ForeignTaxProductCategoryId\"", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductCategoryRead? ForeignTaxProductCategory", face, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void The_configured_target_gets_the_rows_that_point_at_it()
    {
        var run = Run(Excise);

        var category = run.GeneratedSource("Excise_ForeignTax_ForeignTaxCategoryRead");
        var productCategory = run.GeneratedSource("Excise_ProductCategoryRead");

        Assert.Contains("IReadOnlyList<global::Excise.ProductRead> Products { get; init; }", category, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<global::Excise.ProductRead> Products { get; init; }", productCategory, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_nothing_is_configured_on_is_still_inferred_from_its_name()
    {
        var run = Run(Excise);

        var face = run.GeneratedSource("Excise_ProductRead");

        Assert.Contains("public global::Excise.ProductCategoryRead? ProductCategory { get; init; }", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_id_that_keeps_a_legacy_name_is_named_after_its_target_and_gets_the_inverse()
    {
        var run = Run(Providers);

        var face = run.GeneratedSource("Excise_ProviderCountryRead");
        var provider = run.GeneratedSource("Excise_ProviderRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::Excise.ProviderRead Provider { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.Contains("public global::Excise.CountryRead Country { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<global::Excise.ProviderCountryRead> ProviderCountries { get; init; }", provider, StringComparison.Ordinal);
        Assert.Contains("\"Provider\", typeof(global::Excise.ProviderRead), \"TransactionTypeId\", \"ProviderCountries\"", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relationship_configured_from_the_principal_side_lands_on_the_dependent()
    {
        var run = Run(Providers
            .Replace("b.HasOne<Provider>().WithMany().HasForeignKey(c => c.TransactionTypeId);", "b.ToTable(\"Pc\");", StringComparison.Ordinal)
            .Replace(
                "public int? TenantId { get; private set; }\n}",
                "public int? TenantId { get; private set; }\n    public static void Configure(EntityTypeBuilder<Provider> b) =>\n        b.HasMany<ProviderCountry>().WithOne().HasForeignKey(c => c.TransactionTypeId);\n}",
                StringComparison.Ordinal));

        var face = run.GeneratedSource("Excise_ProviderCountryRead");

        Assert.Contains("public global::Excise.ProviderRead Provider { get; init; } = null!;", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_id_joined_on_another_key_gets_no_navigation_and_no_guess()
    {
        var run = Run(Excise.Replace(
            ".HasForeignKey(p => p.ForeignTaxProductCategoryId);",
            ".HasForeignKey(p => p.ForeignTaxProductCategoryId).HasPrincipalKey(c => c.Name);",
            StringComparison.Ordinal));

        var face = run.GeneratedSource("Excise_ProductRead");

        Assert.DoesNotContain(" ForeignTaxProductCategory {", face, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public void Two_configured_ids_to_one_target_are_named_after_themselves()
    {
        var run = Run("""
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed class User : Aggregate<int>
            {
                private User() { }
            }
            public sealed class Note : Aggregate<int>
            {
                private Note() { }
                public int OwnerId { get; private set; }
                public int? EditorId { get; private set; }
                public static void Configure(EntityTypeBuilder<Note> b)
                {
                    b.HasOne<User>().WithMany().HasForeignKey(n => n.OwnerId);
                    b.HasOne<User>().WithMany().HasForeignKey(n => n.EditorId);
                }
            }
            """);

        var face = run.GeneratedSource("Shop_NoteRead");

        Assert.Contains("public global::Shop.UserRead Owner { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.Contains("public global::Shop.UserRead? Editor { get; init; }", face, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Two_configured_relationships_with_no_free_name_between_them_are_an_error()
    {
        var run = Run("""
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed class User : Aggregate<int>
            {
                private User() { }
            }
            public sealed class Note : Aggregate<int>
            {
                private Note() { }
                public int Owner { get; private set; }
                public int Editor { get; private set; }
                public static void Configure(EntityTypeBuilder<Note> b)
                {
                    b.HasOne<User>().WithMany().HasForeignKey(n => n.Owner);
                    b.HasOne<User>().WithMany().HasForeignKey(n => n.Editor);
                }
            }
            """);

        var face = run.GeneratedSource("Shop_NoteRead");
        var errors = run.Diagnostics.Where(d => d.Id == "RASK112").ToList();

        Assert.DoesNotContain("UserRead", face, StringComparison.Ordinal);
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(DiagnosticSeverity.Error, e.Severity));
        Assert.Equal(
            "The relationship configured on 'Note.Owner' gets no navigation on 'NoteRead': 'User' is already taken by a member or another relationship. Rename the id, or what holds the name.",
            errors[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_configured_id_whose_names_are_both_columns_already_is_an_error()
    {
        var run = Run(Providers.Replace(
            "public string VatNumber { get; private set; } = \"\";",
            "public string VatNumber { get; private set; } = \"\";\n    public string Provider { get; private set; } = \"\";\n    public string TransactionType { get; private set; } = \"\";",
            StringComparison.Ordinal));

        var error = Assert.Single(run.Diagnostics, d => d.Id == "RASK112");

        Assert.Equal(
            "The relationship configured on 'ProviderCountry.TransactionTypeId' gets no navigation on 'ProviderCountryRead': 'Provider' and 'TransactionType' are already taken by a member or another relationship. Rename the id, or what holds the name.",
            error.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void An_id_named_after_its_target_keeps_the_name_and_a_second_id_to_it_is_named_after_itself()
    {
        var run = Run("""
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed class Payee : Aggregate<int>
            {
                private Payee() { }
            }
            public sealed class Payroll : Aggregate<int>
            {
                private Payroll() { }
                public int PayeeId { get; private set; }
                public int? AuditorId { get; private set; }
                public static void Configure(EntityTypeBuilder<Payroll> b)
                {
                    b.HasOne<Payee>().WithMany().HasForeignKey(p => p.AuditorId);
                    b.HasOne<Payee>().WithMany().HasForeignKey(p => p.PayeeId);
                }
            }
            """);

        var face = run.GeneratedSource("Shop_PayrollRead");
        var payee = run.GeneratedSource("Shop_PayeeRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::Shop.PayeeRead Payee { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.Contains("public global::Shop.PayeeRead? Auditor { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<global::Shop.PayrollRead> Payrolls { get; init; }", payee, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<global::Shop.PayrollRead> PayrollsAuditor { get; init; }", payee, StringComparison.Ordinal);
    }
}
