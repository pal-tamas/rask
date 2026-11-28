using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Tests;

// Each value object of one value is given a System.Text.Json converter of its own that writes it as that value, and the
// API client's codec — whose server reads the body with System.Text.Json — carries exactly those value objects bare too,
// so the client and the server agree.
public sealed class ValueObjectJsonGeneratorTests
{
    private const string Values = """
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Rask.Values;
        namespace Shop;

        public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100);
        public sealed partial record Contact(string Value) : Email(Value);
        public sealed partial record Mobile(string Value) : Phone(Value);
        public sealed partial record Site(string Value) : Url(Value);
        public sealed partial record Note(string Value) : Multiline(Value);
        public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2);
        public sealed partial record Pieces(long Value) : Integer(Value, Min: 0);
        public sealed partial record Seats(int Value) : Integer(Value, Min: 1);
        public sealed partial record InvoiceDay(DateOnly Value) : Day(Value);
        public sealed partial record FueledAt(DateTime Value) : Moment(Value);
        public sealed partial record Rank(int Position) : Value;
        public sealed partial record SignInPassword(string Value) : Password(Value, MinLength: 8);
        public sealed partial record Score(int? Value) : Value;
        public sealed partial record Span(int Start, int End) : Rask.Values.Range(Start, End);
        [JsonConverter(typeof(OwnConverter))]
        public sealed partial record Own(string Value) : Text(Value);
        public sealed class OwnConverter : JsonConverter<Own>
        {
            public override Own Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Own.Create(reader.GetString()!);
            public override void Write(Utf8JsonWriter writer, Own value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
        }
        """;

    private static readonly Lazy<GeneratorRun> Generated = new(static () =>
        GeneratorHarness.Run(Values, [new ValueObjectFactoryGenerator()], "Rask.Data", "Rask.Cqrs", "Rask.Wire"));

    [Theory]
    [InlineData("DriverName", "string", "global::Rask.Wire.WireJson.ReadString(ref reader, property) ?? throw")]
    [InlineData("Contact", "string", "global::Rask.Wire.WireJson.ReadString(ref reader, property) ?? throw")]
    [InlineData("Mobile", "string", "global::Rask.Wire.WireJson.ReadString(ref reader, property) ?? throw")]
    [InlineData("Site", "string", "global::Rask.Wire.WireJson.ReadString(ref reader, property) ?? throw")]
    [InlineData("Note", "string", "global::Rask.Wire.WireJson.ReadString(ref reader, property) ?? throw")]
    [InlineData("Litres", "decimal", "global::Rask.Wire.WireJson.ReadDecimal(ref reader, property);")]
    [InlineData("Pieces", "long", "global::Rask.Wire.WireJson.ReadInt64(ref reader, property);")]
    [InlineData("Seats", "int", "global::Rask.Wire.WireJson.ReadInt32(ref reader, property);")]
    [InlineData("InvoiceDay", "global::System.DateOnly", "global::Rask.Wire.WireJson.ReadDateOnly(ref reader, property);")]
    [InlineData("Rank", "int", "global::Rask.Wire.WireJson.ReadInt32(ref reader, property);")]
    public void A_value_object_of_one_value_is_given_a_converter_that_writes_and_makes_it_from_its_value(string name, string held, string read)
    {
        var run = Generated.Value;

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains($"[global::System.Text.Json.Serialization.JsonConverter(typeof({name}.__Json))]", generated, StringComparison.Ordinal);
        Assert.Contains($"internal sealed class __Json : global::Rask.Values.ValueJsonConverter<{name}, {held}>", generated, StringComparison.Ordinal);
        Assert.Contains(read, generated, StringComparison.Ordinal);
        Assert.Contains($"protected override {name} Make({held} held) => new {name}(held);", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_moment_keeps_the_converter_that_writes_its_instant()
    {
        var run = Generated.Value;

        var generated = run.GeneratedSource("FueledAt.Value.g.cs");

        Assert.Contains("internal sealed class __Json : global::Rask.Values.MomentJsonConverter<FueledAt>", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SignInPassword")]
    [InlineData("Score")]
    [InlineData("Span")]
    [InlineData("Own")]
    public void A_password_a_nullable_member_several_values_or_a_converter_of_its_own_is_given_none(string name)
    {
        var run = Generated.Value;

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("__Json", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DriverName")]
    [InlineData("Contact")]
    [InlineData("Litres")]
    [InlineData("Pieces")]
    [InlineData("InvoiceDay")]
    [InlineData("FueledAt")]
    [InlineData("Rank")]
    public void The_api_client_codec_carries_bare_every_value_object_system_text_json_writes_bare(string name)
    {
        var run = Generated.Value;
        var type = run.Compilation.GetTypeByMetadataName("Shop." + name)!;

        var forApi = WireShape.Classify(type, allowFile: false, compilation: run.Compilation);

        Assert.Equal(WireKind.OneValue, forApi.Kind);
        Assert.True(forApi.ValueObject);
    }

    [Theory]
    [InlineData("SignInPassword")]
    [InlineData("Own")]
    [InlineData("Span")]
    public void The_api_client_codec_keeps_as_an_object_what_system_text_json_writes_as_one(string name)
    {
        var run = Generated.Value;
        var type = run.Compilation.GetTypeByMetadataName("Shop." + name)!;

        var forApi = WireShape.Classify(type, allowFile: false, compilation: run.Compilation);

        Assert.NotEqual(WireKind.OneValue, forApi.Kind);
    }

    [Fact]
    public void A_value_object_whose_generated_converter_is_in_sight_as_from_a_referenced_assembly_is_still_carried_bare()
    {
        var run = Generated.Value;
        var options = (CSharpParseOptions)run.Compilation.SyntaxTrees.First().Options;
        var built = run.Compilation.AddSyntaxTrees(
            run.RunResult.Results.SelectMany(static r => r.GeneratedSources)
                .Select(s => CSharpSyntaxTree.ParseText(s.SourceText, options, path: s.HintName)));
        var name = built.GetTypeByMetadataName("Shop.DriverName")!;

        var forApi = WireShape.Classify(name, allowFile: false, compilation: built);

        Assert.Equal(WireKind.OneValue, forApi.Kind);
    }
}
