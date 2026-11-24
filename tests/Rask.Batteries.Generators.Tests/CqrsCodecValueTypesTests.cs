using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Tests;

// A contract may carry what the domain holds: a record struct, a one-value value object and a typed id, the last two
// as the bare value they hold ("ABC-123", 42), rebuilt unchecked through the value object's generated Create.
public class CqrsCodecValueTypesTests
{
    private const string Contracts = """
        using Rask.Cqrs;
        using Rask.Data;
        using Rask.Values;

        namespace Fleet.Contracts
        {
            public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000);

            public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2);

            public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);

            public readonly partial record struct VehicleId(int Value);

            public readonly record struct TripKey(System.Guid Value);

            public readonly record struct Plate(string Value);

            public readonly record struct Odometer(int Km, int Metres);

            public sealed record Assign(
                VehicleId Vehicle,
                VehicleId? Spare,
                TripKey Trip,
                Plate Plate,
                DriverName Driver,
                DriverName? CoDriver,
                Litres Fuel,
                KmRange Km,
                Odometer Reading,
                System.Collections.Generic.List<VehicleId> Fleet) : ICommand<VehicleId>;
        }
        """;

    private static readonly Dictionary<string, string> Options = new() { ["build_property.RaskCqrsCodec"] = "true" };

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            [new ValueObjectFactoryGenerator(), new CqrsCodecGenerator()],
            Options,
            "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Data", "Rask.Wire", "Microsoft.Extensions.DependencyInjection.Abstractions");

    [Fact]
    public void A_contract_carrying_record_structs_value_objects_and_typed_ids_has_a_codec_that_compiles()
    {
        var run = Run(Contracts);

        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "RASK053");
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_one_value_value_object_is_written_as_the_value_it_holds()
    {
        var codec = Run(Contracts).GeneratedSource("__RaskCqrsCodecs");

        Assert.Contains("global::Fleet.Contracts.DriverName value", codec, StringComparison.Ordinal);
        Assert.Contains("(writer, value.Value, files);", codec, StringComparison.Ordinal);
        Assert.DoesNotContain("writer.WritePropertyName(\"value\")", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_object_is_read_through_its_create_inside_a_loading_scope()
    {
        var codec = Run(Contracts).GeneratedSource("__RaskCqrsCodecs");

        Assert.Contains("using var __loading = global::Rask.Values.GeneratedValueRules.Loading();", codec, StringComparison.Ordinal);
        Assert.Contains("return global::Fleet.Contracts.DriverName.Create(held);", codec, StringComparison.Ordinal);
        Assert.Contains("return global::Fleet.Contracts.Litres.Create(held);", codec, StringComparison.Ordinal);
        Assert.Contains("return global::Fleet.Contracts.KmRange.Create(v_Start, v_End);", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_id_is_read_through_its_constructor_with_no_loading_scope()
    {
        var codec = Run(Contracts).GeneratedSource("__RaskCqrsCodecs");

        Assert.Contains("return new global::Fleet.Contracts.VehicleId(held);", codec, StringComparison.Ordinal);
        Assert.Contains("return new global::Fleet.Contracts.TripKey(held);", codec, StringComparison.Ordinal);
        Assert.DoesNotContain("VehicleId.Create(", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_struct_of_several_values_travels_as_an_object()
    {
        var codec = Run(Contracts).GeneratedSource("__RaskCqrsCodecs");

        Assert.Contains("writer.WritePropertyName(\"km\");", codec, StringComparison.Ordinal);
        Assert.Contains("writer.WritePropertyName(\"metres\");", codec, StringComparison.Ordinal);
        Assert.Contains("return new global::Fleet.Contracts.Odometer(v_Km, v_Metres);", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_struct_holding_a_list_travels_as_an_object_since_a_bare_list_would_lose_its_name()
    {
        const string source = """
            using Rask.Cqrs;

            namespace Fleet.Contracts
            {
                public readonly record struct Stops(System.Collections.Generic.List<string> Names);

                public sealed record Plan(Stops Stops) : ICommand;
            }
            """;

        var run = Run(source);
        var codec = run.GeneratedSource("__RaskCqrsCodecs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("writer.WritePropertyName(\"names\");", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_value_record_class_that_is_no_value_object_keeps_its_object_shape()
    {
        const string source = """
            using Rask.Cqrs;

            namespace Fleet.Contracts
            {
                public sealed record Note(string Text);

                public sealed record Annotate(Note Note) : ICommand;
            }
            """;

        var codec = Run(source).GeneratedSource("__RaskCqrsCodecs");

        Assert.Contains("writer.WritePropertyName(\"text\");", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_client_walk_keeps_value_objects_and_typed_ids_as_objects_for_its_system_text_json_server()
    {
        var run = Run(Contracts);
        var vehicle = run.Compilation.GetTypeByMetadataName("Fleet.Contracts.VehicleId")!;

        var forApi = WireShape.Classify(vehicle, allowFile: false, compilation: run.Compilation);
        var forCqrs = WireShape.Classify(vehicle, allowFile: false, compilation: run.Compilation, unwrapValues: true);

        Assert.Equal(WireKind.Object, forApi.Kind);
        Assert.Equal(WireKind.OneValue, forCqrs.Kind);
    }
}
