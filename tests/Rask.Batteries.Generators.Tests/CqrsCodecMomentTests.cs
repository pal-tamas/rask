using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Cqrs;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Tests;

// A moment travels as the UTC instant it holds, not as its wall-clock Value: the hour Europe/Budapest shows twice in
// autumn (02:30 is 00:30Z, then 01:30Z) comes back as the instant it was, and the wall-clock form a page loaded before
// the change still sends is read as a wall time of Clock.Zone. Run for real: the generated codec, compiled and loaded.
public sealed class CqrsCodecMomentTests
{
    private const string Contracts = """
        using Rask.Cqrs;
        using Rask.Values;

        namespace Fleet.Contracts
        {
            public sealed partial record FueledAt(System.DateTime Value) : Moment(Value);

            public sealed record Refuel(FueledAt At) : ICommand<FueledAt>;
        }
        """;

    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private static readonly Lazy<RemoteContract> Refuel = new(Load);

    [Theory]
    [InlineData("2026-10-25T00:30:00Z")]
    [InlineData("2026-10-25T01:30:00Z")]
    public void Both_occurrences_of_the_autumn_double_hour_round_trip_through_the_generated_codec(string instant)
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var written = Write(Read(Quoted(instant)));

        Assert.Equal(Quoted(instant), written);
    }

    [Theory]
    [InlineData("2026-10-25T02:30:00", "2026-10-25T00:30:00Z")] // the old wall-clock form: its first occurrence
    [InlineData("2026-10-25T02:30:00+01:00", "2026-10-25T01:30:00Z")] // an offset names the instant exactly
    public void A_moment_read_in_another_form_is_written_back_as_the_instant_it_names(string sent, string instant)
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var written = Write(Read(Quoted(sent)));

        Assert.Equal(Quoted(instant), written);
    }

    [Fact]
    public void A_moment_is_written_by_its_instant_in_the_codec_and_the_api_client_alike()
    {
        var run = Run();
        var moment = run.Compilation.GetTypeByMetadataName("Fleet.Contracts.FueledAt")!;

        var forApi = WireShape.Classify(moment, allowFile: false, compilation: run.Compilation);
        var codec = run.GeneratedSource("__RaskCqrsCodecs");

        Assert.Equal(WireKind.OneValue, forApi.Kind);
        Assert.True(forApi.Moment);
        Assert.Contains("writer.WriteStringValue(value.Instant.UtcDateTime);", codec, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Wire.WireJson.ReadMoment(ref reader, property);", codec, StringComparison.Ordinal);
    }

    private static string Quoted(string text) => "\"" + text + "\"";

    private static object Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return Refuel.Value.ReadResult!(ref reader)!;
    }

    private static string Write(object moment)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Refuel.Value.WriteResult!(writer, moment);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Contracts,
            [new ValueObjectFactoryGenerator(), new CqrsCodecGenerator()],
            new Dictionary<string, string> { ["build_property.RaskCqrsCodec"] = "true" },
            "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Data", "Rask.Wire", "Microsoft.Extensions.DependencyInjection.Abstractions");

    // The contracts and everything generated for them, compiled and loaded; their module initializer registers the codec.
    private static RemoteContract Load()
    {
        var run = Run();
        var generated = run.RunResult.Results.SelectMany(static r => r.GeneratedSources)
            .Select(static s => CSharpSyntaxTree.ParseText(s.SourceText, new CSharpParseOptions(LanguageVersion.Latest), path: s.HintName));
        using var image = new MemoryStream();
        var emitted = run.Compilation.AddSyntaxTrees(generated).Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        image.Position = 0;
        var assembly = new AssemblyLoadContext("moment-codec", isCollectible: true).LoadFromStream(image);
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        var message = assembly.GetType("Fleet.Contracts.Refuel", throwOnError: true)!;
        return RemoteContractRegistry.All.Single(c => c.MessageType == message);
    }
}
