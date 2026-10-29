using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// The collection of the rows that point at a face: two relationships that would both take the convention's name are
/// told apart by their own words instead of both losing their collections.
/// </summary>
public partial class InverseCollectionNameTests
{
    // Eszaf's shape: two keys to one catalog column, neither ending in the target's whole name.
    private const string Totals = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Reports;
        public sealed class CatalogColumn : Aggregate<int>
        {
            private CatalogColumn() { }
            public string Name { get; private set; } = "";
        }
        public sealed class ReportColumnTotal : Aggregate<int>
        {
            private ReportColumnTotal() { }
            public int ReportColumnColumnId { get; private set; }
            public int? TotalColumnId { get; private set; }
            public static void Configure(EntityTypeBuilder<ReportColumnTotal> b)
            {
                b.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.ReportColumnColumnId);
                b.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.TotalColumnId);
            }
        }
        """;

    // Every shape a collection name was already derived for before two rivals could be told apart.
    private const string Corpus = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Corpus;
        public sealed class Fueling : Aggregate<string> { private Fueling() { } }
        public sealed class Distribution : Aggregate<string>
        {
            private Distribution() { }
            public string FromFuelingId { get; private set; } = "";
            public string ToFuelingId { get; private set; } = "";
        }
        public sealed class Customer : Aggregate<int> { private Customer() { } }
        public sealed class Order : Aggregate<int>
        {
            private Order() { }
            public int CustomerId { get; private set; }
            public int? BillingCustomerId { get; private set; }
        }
        public sealed class Payee : Aggregate<int> { private Payee() { } }
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
        public sealed class TaxCategory : Aggregate<int> { private TaxCategory() { } }
        public sealed class Product : Aggregate<int>
        {
            private Product() { }
            public int? ForeignProductCategoryId { get; private set; }
            public static void Configure(EntityTypeBuilder<Product> b) =>
                b.HasOne<TaxCategory>().WithMany().HasForeignKey(p => p.ForeignProductCategoryId);
        }
        public sealed class Provider : Aggregate<int> { private Provider() { } }
        public sealed class ProviderCountry : Aggregate<int>
        {
            private ProviderCountry() { }
            public int TransactionTypeId { get; private set; }
            public static void Configure(EntityTypeBuilder<ProviderCountry> b) =>
                b.HasOne<Provider>().WithMany().HasForeignKey(c => c.TransactionTypeId);
        }
        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ReadModelGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    private static List<string> Collections(string face) =>
        [.. CollectionProperty().Matches(face).Select(static m => m.Groups[1].Value)];

    [GeneratedRegex(@"IReadOnlyList<[^>]+> (\w+) \{ get; init; \}")]
    private static partial Regex CollectionProperty();

    [Fact]
    public void Two_configured_keys_to_one_target_each_get_a_collection_named_by_their_own_words()
    {
        var run = Run(Totals);

        var column = run.GeneratedSource("Reports_CatalogColumnRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK108");
        Assert.Equal(["ReportColumnTotals", "ReportColumnTotalsTotal"], Collections(column));
        Assert.Contains("\"ReportColumnColumnId\", \"ReportColumnTotals\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"TotalColumnId\", \"ReportColumnTotalsTotal\"", registry, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void The_first_key_in_source_order_keeps_the_conventional_name()
    {
        var run = Run(Totals.Replace(
            "b.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.ReportColumnColumnId);\n        b.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.TotalColumnId);",
            "b.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.TotalColumnId);\n        b.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.ReportColumnColumnId);",
            StringComparison.Ordinal));

        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("\"TotalColumnId\", \"ReportColumnTotals\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"ReportColumnColumnId\", \"ReportColumnTotalsReportColumn\"", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_further_key_to_the_same_target_is_named_by_its_own_words()
    {
        var run = Run(Totals.Replace(
            "public int? TotalColumnId { get; private set; }",
            "public int? TotalColumnId { get; private set; }\n    public int? AverageColumnId { get; private set; }",
            StringComparison.Ordinal).Replace(
            "HasForeignKey(t => t.TotalColumnId);",
            "HasForeignKey(t => t.TotalColumnId);\n        b.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.AverageColumnId);",
            StringComparison.Ordinal));

        var column = run.GeneratedSource("Reports_CatalogColumnRead");

        Assert.Empty(run.Diagnostics);
        Assert.Equal(["ReportColumnTotals", "ReportColumnTotalsTotal", "ReportColumnTotalsAverage"], Collections(column));
    }

    [Fact]
    public void A_second_key_whose_own_words_a_column_holds_is_still_reported()
    {
        var run = Run(Totals.Replace(
            "public string Name { get; private set; } = \"\";",
            "public string Name { get; private set; } = \"\";\n    public int ReportColumnTotalsTotal { get; private set; }",
            StringComparison.Ordinal));

        var warning = Assert.Single(run.Diagnostics, d => d.Id == "RASK108");
        var column = run.GeneratedSource("Reports_CatalogColumnRead");

        Assert.Contains("Rename the id (ReportColumnTotal.TotalColumnId)", warning.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(["ReportColumnTotals"], Collections(column));
    }

    [Fact]
    public void Every_collection_name_a_shape_without_rivals_already_had_is_unchanged()
    {
        var run = Run(Corpus);

        var names = new[] { "Fueling", "Customer", "Payee", "TaxCategory", "Provider" }
            .SelectMany(t => Collections(run.GeneratedSource($"Corpus_{t}Read")).Select(c => $"{t}.{c}"))
            .ToList();

        Assert.Empty(run.Diagnostics);
        Assert.Equal(
            [
                "Fueling.DistributionsFrom", "Fueling.DistributionsTo",
                "Customer.Orders", "Customer.OrdersBilling",
                "Payee.PayrollsAuditor", "Payee.Payrolls",
                "TaxCategory.Products",
                "Provider.ProviderCountries",
            ],
            names);
    }
}
