namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// The relationship a root's <c>Configure</c> declares over the children it holds — <c>HasMany(c =&gt; c.Countries)</c> — is
/// the child's way back to its root, which the read face already has; it is never a second navigation over the same id.
/// </summary>
public class HeldChildRelationshipReadTests
{
    // The shape of an existing app: the root's part keeps the table's column name, so it is a named part, and the root
    // says which column holds its children.
    private const string NamedPart = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Excise;
        public sealed class Country : Aggregate<int>
        {
            private Country() { }
        }
        public sealed class ForeignTaxCategory : Aggregate<int>
        {
            private readonly System.Collections.Generic.List<ForeignTaxCategoryCountry> _countries = [];
            private ForeignTaxCategory() { }
            public string Name { get; private set; } = "";
            public System.Collections.Generic.IReadOnlyList<ForeignTaxCategoryCountry> Countries => _countries;
            public static void Configure(EntityTypeBuilder<ForeignTaxCategory> b) =>
                b.HasMany(c => c.Countries).WithOne().HasForeignKey(row => row.ForeignTaxProductCategoryId);
        }
        public sealed partial class ForeignTaxCategoryCountry : Entity<(int ForeignTaxProductCategoryId, Country)>
        {
            private ForeignTaxCategoryCountry() { }
            public int ForeignTaxProductCategoryId { get; private set; }
            public static void Configure(EntityTypeBuilder<ForeignTaxCategoryCountry> b) =>
                b.HasOne<Country>().WithMany().HasForeignKey(row => row.CountryId);
        }
        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, [new ReadModelGenerator(), new CompositeKeyGenerator()], "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_named_key_part_the_root_holds_its_children_by_is_the_way_back_to_the_root_and_nothing_else()
    {
        var run = Run(NamedPart);

        var face = run.GeneratedSource("Excise_ForeignTaxCategoryCountryRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::Excise.ForeignTaxCategoryRead ForeignTaxCategory { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.Contains("public global::Excise.CountryRead Country { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.DoesNotContain(" ForeignTaxProductCategory {", face, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::Excise.ForeignTaxCategoryRead), \"ForeignTaxProductCategoryId\"", registry, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void The_root_holds_its_children_once_and_gets_no_second_collection_of_them()
    {
        var run = Run(NamedPart);

        var root = run.GeneratedSource("Excise_ForeignTaxCategoryRead");

        Assert.Contains("List<global::Excise.ForeignTaxCategoryCountryRead> Countries { get; init; }", root, StringComparison.Ordinal);
        Assert.DoesNotContain("ForeignTaxCategoryCountries", root, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_part_spelled_as_the_aggregate_and_held_by_a_configured_column_is_no_error()
    {
        var run = Run(NamedPart
            .Replace("Entity<(int ForeignTaxProductCategoryId, Country)>", "Entity<(ForeignTaxCategory, Country)>", StringComparison.Ordinal)
            .Replace("    public int ForeignTaxProductCategoryId { get; private set; }\n", string.Empty, StringComparison.Ordinal)
            .Replace("row => row.ForeignTaxProductCategoryId", "row => row.ForeignTaxCategoryId", StringComparison.Ordinal));

        var face = run.GeneratedSource("Excise_ForeignTaxCategoryCountryRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::Excise.ForeignTaxCategoryRead ForeignTaxCategory { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.Single(face.Split("ForeignTaxCategoryRead ", StringSplitOptions.None).Skip(1));
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_grandchild_held_by_a_configured_key_of_several_columns_gets_only_its_way_back_to_the_child()
    {
        var run = Run(NamedPart
            .Replace(
                "    public int ForeignTaxProductCategoryId { get; private set; }\n    public static void Configure(EntityTypeBuilder<ForeignTaxCategoryCountry> b) =>\n        b.HasOne<Country>().WithMany().HasForeignKey(row => row.CountryId);",
                """
                    private readonly System.Collections.Generic.List<ForeignTaxCategoryCountryRate> _rates = [];
                    public int ForeignTaxProductCategoryId { get; private set; }
                    public System.Collections.Generic.IReadOnlyList<ForeignTaxCategoryCountryRate> Rates => _rates;
                    public static void Configure(EntityTypeBuilder<ForeignTaxCategoryCountry> b)
                    {
                        b.HasOne<Country>().WithMany().HasForeignKey(row => row.CountryId);
                        b.HasMany(c => c.Rates).WithOne().HasForeignKey(r => new { r.ForeignTaxProductCategoryId, r.CountryId });
                    }
                }
                public sealed partial class ForeignTaxCategoryCountryRate : Entity<(int ForeignTaxProductCategoryId, int CountryId, int Year)>
                {
                    private ForeignTaxCategoryCountryRate() { }
                    public int ForeignTaxProductCategoryId { get; private set; }
                    public int CountryId { get; private set; }
                    public int Year { get; private set; }
                """,
                StringComparison.Ordinal));

        var face = run.GeneratedSource("Excise_ForeignTaxCategoryCountryRateRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::Excise.ForeignTaxCategoryCountryRead ForeignTaxCategoryCountry { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.DoesNotContain(" ForeignTaxProductCategory {", face, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }
}
