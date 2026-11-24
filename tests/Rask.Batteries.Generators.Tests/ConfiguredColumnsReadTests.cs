namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A value object's columns named after a pattern in the entity's own <c>Configure</c> —
/// <c>b.Columns(n =&gt; n.Km, "{Member}Km")</c> — name the read face's members too: <c>StartKm</c>, not <c>KmStart</c>.
/// </summary>
public class ConfiguredColumnsReadTests
{
    private const string Notes = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Fleet;
        public static class Legacy { public const string Km = "{Member}Km"; }
        public sealed record KmRange(int Start, int End);
        public sealed record Money(decimal Amount, string Currency);
        public sealed record Gate(int @in, int @out);
        public sealed class DeliveryNote : Aggregate<int>
        {
            private DeliveryNote() { }
            public KmRange Km { get; private set; } = new(0, 1);
            public KmRange Trip { get; private set; } = new(0, 1);
            public Money Price { get; private set; } = new(0m, "HUF");
            public Money Toll { get; private set; } = new(0m, "HUF");
            public Gate Door { get; private set; } = new(0, 1);
            public static void Configure(EntityTypeBuilder<DeliveryNote> b)
            {
                b.Columns(n => n.Km, Legacy.Km).Columns(n => n.Trip, "{Member}Trip");
                b.Columns(n => n.Price, "Price {Member}");
                b.Columns(n => n.Door, "{Member}");
            }
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(Notes, new ReadModelGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_patterned_value_object_names_its_read_members_and_columns_after_the_pattern()
    {
        var run = Run();

        var face = run.GeneratedSource("Fleet_DeliveryNoteRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public int StartKm { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public int EndTrip { get; init; }", face, StringComparison.Ordinal);
        Assert.DoesNotContain("KmStart", face, StringComparison.Ordinal);
        Assert.Contains("\"StartKm\", \"Km.Start\", \"StartKm\"", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_that_makes_no_member_name_keeps_the_member_and_names_only_the_column()
    {
        var run = Run();

        var face = run.GeneratedSource("Fleet_DeliveryNoteRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Contains("public decimal PriceAmount { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("\"PriceAmount\", \"Price.Amount\", \"Price Amount\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"TollAmount\", \"Toll.Amount\", \"Toll_Amount\"", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_that_makes_a_csharp_keyword_keeps_the_member_and_names_only_the_column()
    {
        var run = Run();

        var face = run.GeneratedSource("Fleet_DeliveryNoteRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public int Doorin { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("\"Doorin\", \"Door.in\", \"in\"", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("public int in ", face, StringComparison.Ordinal);
    }
}
