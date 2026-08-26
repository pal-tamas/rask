using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Cqrs.Generators.Tests;

/// <summary>
///     What the TypeScript emitter produces for each wire shape.
/// </summary>
/// <remarks>
///     These assert the mapping decisions rather than the formatting: which C# types become a JS
///     <c>Date</c>, which deliberately stay strings, and which properties end up in the descriptor
///     the client revives against.
/// </remarks>
public class TypeScriptEmitterTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        """;

    /// <summary>Classifies one named type from a source snippet and emits its TypeScript.</summary>
    private static (string Declarations, TypeScriptEmitter Emitter) Emit(string source, string typeName = "Probe")
    {
        var tree = CSharpSyntaxTree.ParseText(Preamble + source);
        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>();

        // Nullable contexts ON, because every Rask project and every scaffolded template has them
        // on — with them off, `string?` is indistinguishable from `string` and the emitted type
        // would be testing something no real consumer compiles.
        var compilation = CSharpCompilation.Create(
            "Probe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var symbol = compilation.GetTypeByMetadataName(typeName)
                     ?? throw new InvalidOperationException($"'{typeName}' did not compile.");

        var emitter = new TypeScriptEmitter();
        emitter.Ensure(WireShape.Classify(symbol, allowFile: false));
        return (emitter.Declarations, emitter);
    }

    [Fact]
    public void An_instant_becomes_a_Date()
    {
        var (ts, _) = Emit("public sealed record Probe(DateTimeOffset PlacedAt, DateTime SeenAt);");

        Assert.Contains("placedAt: Date;", ts, StringComparison.Ordinal);
        Assert.Contains("seenAt: Date;", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_calendar_date_stays_a_string()
    {
        var (ts, _) = Emit("public sealed record Probe(DateOnly DeliverBy);");

        // new Date("2026-08-25") is UTC midnight, so anyone west of UTC renders it as the 24th —
        // the off-by-one-day the Gantt sample already documents. A calendar date is not an instant.
        Assert.Contains("deliverBy: DateOnly;", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("deliverBy: Date;", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_time_of_day_and_a_duration_stay_strings()
    {
        var (ts, _) = Emit("public sealed record Probe(TimeOnly OpensAt, TimeSpan Sla);");

        Assert.Contains("opensAt: TimeOnly;", ts, StringComparison.Ordinal);
        Assert.Contains("sla: Duration;", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Property_names_are_the_wire_names()
    {
        var (ts, _) = Emit("public sealed record Probe(int PageSize);");

        // camelCase comes from WireShape, the same place the codec reads it. Deriving it here
        // instead would be a second naming rule to keep in step.
        Assert.Contains("pageSize: number;", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int", "number")]
    [InlineData("long", "number")]
    [InlineData("decimal", "number")]
    [InlineData("double", "number")]
    [InlineData("bool", "boolean")]
    [InlineData("string", "string")]
    [InlineData("Guid", "Guid")]
    public void Scalars_map_to_their_wire_form(string clr, string ts)
    {
        var (emitted, _) = Emit($"public sealed record Probe({clr} Value);");

        Assert.Contains($"value: {ts};", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_is_a_union_with_null()
    {
        var (ts, _) = Emit("public sealed record Probe(int? Count, string? Note);");

        Assert.Contains("count: number | null;", ts, StringComparison.Ordinal);
        Assert.Contains("note: string | null;", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_of_nullables_is_parenthesised()
    {
        var (ts, _) = Emit("public sealed record Probe(List<int?> Values);");

        // (number | null)[] and not number | null[], which would be a union with an array.
        Assert.Contains("values: (number | null)[];", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dictionary_is_a_record()
    {
        var (ts, _) = Emit("public sealed record Probe(Dictionary<string, int> Totals);");

        Assert.Contains("totals: Record<string, number>;", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enum_is_emitted_with_its_members_and_its_numbers()
    {
        var (ts, _) = Emit(
            """
            public enum Fulfilment { Pending = 0, Shipped = 7 }
            public sealed record Probe(Fulfilment Status);
            """);

        Assert.Contains("export enum Fulfilment {", ts, StringComparison.Ordinal);
        Assert.Contains("Pending = 0,", ts, StringComparison.Ordinal);

        // The number matters, not the position: the wire carries the underlying value, so a
        // renumber is a breaking change and the emitted enum has to say what the values are.
        Assert.Contains("Shipped = 7,", ts, StringComparison.Ordinal);
        Assert.Contains("status: Fulfilment;", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_record_is_emitted_once_and_referenced()
    {
        var (ts, _) = Emit(
            """
            public sealed record Line(string Sku);
            public sealed record Probe(List<Line> Lines, Line? First);
            """);

        Assert.Equal(1, Occurrences(ts, "export interface Line {"));
        Assert.Contains("lines: Line[];", ts, StringComparison.Ordinal);
        Assert.Contains("first: Line | null;", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_self_referencing_shape_never_reaches_the_emitter()
    {
        // The classifier refuses it, so RASK053 fails the build long before any emitter runs — even
        // through a collection, which is stricter than WireCodecEmitter's own comment claims.
        // The emitter still guards, and the guard names the diagnostic rather than emitting `any`:
        // a type that means nothing is worse on a front end than a build error.
        var error = Assert.Throws<InvalidOperationException>(() => Emit(
            """
            public sealed record Probe(string Name, List<Probe> Children);
            """));

        Assert.Contains("RASK053", error.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- descriptor

    [Fact]
    public void The_descriptor_names_the_instants_to_revive()
    {
        var (_, emitter) = Emit("public sealed record Probe(DateTimeOffset PlacedAt, string Sku);");

        var shape = emitter.Shapes["Probe"];
        Assert.Equal(["placedAt"], shape.Instants);
    }

    [Fact]
    public void A_string_that_merely_looks_like_a_date_is_not_revived()
    {
        var (_, emitter) = Emit("public sealed record Probe(string Reference, DateTimeOffset At);");

        // The whole reason the descriptor exists. A regex reviver would convert a product code or
        // an ETag that happens to look like a timestamp; this converts what the C# type said to.
        var shape = emitter.Shapes["Probe"];
        Assert.DoesNotContain("reference", shape.Instants);
        Assert.Contains("at", shape.Instants);
    }

    [Fact]
    public void An_instant_inside_a_list_is_still_named()
    {
        var (_, emitter) = Emit("public sealed record Probe(List<DateTimeOffset> Stamps);");

        // The runtime walks arrays and record values generically, so one entry covers a bare value,
        // a list of them and a dictionary of them alike.
        Assert.Equal(["stamps"], emitter.Shapes["Probe"].Instants);
    }

    [Fact]
    public void A_nested_shape_is_named_so_the_walk_can_continue()
    {
        var (_, emitter) = Emit(
            """
            public sealed record Line(DateTimeOffset ShippedAt);
            public sealed record Probe(List<Line> Lines);
            """);

        Assert.Equal(new NestedShape("Line", 1), emitter.Shapes["Probe"].Nested["lines"]);
        Assert.Equal(["shippedAt"], emitter.Shapes["Line"].Instants);
    }

    [Fact]
    public void A_dictionary_of_shapes_is_counted_as_a_container()
    {
        var (_, emitter) = Emit(
            """
            public sealed record Line(DateTimeOffset ShippedAt);
            public sealed record Probe(Dictionary<string, Line> ByCode);
            """);

        // The count is what the client needs to tell a Dictionary<string, Line> from a Line: both
        // arrive as plain objects, so without it the walk would look for shippedAt on the dictionary
        // itself and revive nothing.
        Assert.Equal(new NestedShape("Line", 1), emitter.Shapes["Probe"].Nested["byCode"]);
    }

    [Fact]
    public void A_nullable_shape_is_not_a_container()
    {
        var (_, emitter) = Emit(
            """
            public sealed record Line(DateTimeOffset ShippedAt);
            public sealed record Probe(Line? Latest);
            """);

        // `Line?` still arrives as one object. Counting it would make the client iterate the object's
        // own properties looking for more Lines.
        Assert.Equal(new NestedShape("Line", 0), emitter.Shapes["Probe"].Nested["latest"]);
    }

    [Fact]
    public void A_list_of_lists_is_counted_twice()
    {
        var (_, emitter) = Emit(
            """
            public sealed record Line(DateTimeOffset ShippedAt);
            public sealed record Probe(List<List<Line>> Batches);
            """);

        Assert.Equal(new NestedShape("Line", 2), emitter.Shapes["Probe"].Nested["batches"]);
    }

    [Fact]
    public void A_shape_with_no_dates_needs_no_work_at_runtime()
    {
        var (_, emitter) = Emit("public sealed record Probe(string Sku, int Quantity);");

        Assert.True(emitter.Shapes["Probe"].IsEmpty);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
