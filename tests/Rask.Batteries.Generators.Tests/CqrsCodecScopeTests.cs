using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Tests;

// Rask.Server references the server transport so a wasm-hosted app can Serve() its client, and its build turns the
// codec off (RaskCqrsCodec=false) in a project with no browser client: a server-rendered app's messages never
// leave the process, so they neither pay for a codec nor have to fit the wire.
public class CqrsCodecScopeTests
{
    private const string Message = """
        using Rask.Cqrs;

        namespace Shop.Contracts
        {
            public interface IShape { }

            public sealed record Rename(System.Guid Id, IShape Shape) : ICommand;
        }
        """;

    [Fact]
    public void A_message_the_wire_cannot_carry_is_an_error_where_the_codec_runs()
    {
        var options = new Dictionary<string, string> { ["build_property.RaskCqrsCodec"] = "true" };

        var run = Run(options);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK053");
    }

    [Fact]
    public void A_project_that_turns_the_codec_off_compiles_any_message_and_emits_no_codec()
    {
        var options = new Dictionary<string, string> { ["build_property.RaskCqrsCodec"] = "false" };

        var run = Run(options);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK053");
        Assert.Empty(run.RunResult.Results.SelectMany(r => r.GeneratedSources));
    }

    private static GeneratorRun Run(IReadOnlyDictionary<string, string> options) =>
        GeneratorHarness.Run(
            Message,
            new CqrsCodecGenerator(),
            options,
            "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Wire", "Microsoft.Extensions.DependencyInjection.Abstractions");
}
