using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Tests;

// The codec walked one hand-unrolled level of nesting, so a message inside a container inside a container got
// no contract — the #949 miss the island and Blazor generators had already fixed.
public class CqrsCodecNestedTypesTests
{
    private const string Messages = """
        using Rask.Cqrs;

        namespace Shop.Contracts
        {
            public static class Orders
            {
                public sealed record Ship(System.Guid Id) : ICommand;

                public static class Returns
                {
                    public sealed record Refund(System.Guid Id) : ICommand;
                }
            }
        }
        """;

    [Fact]
    public void A_message_nested_two_levels_deep_gets_a_contract_like_one_nested_once()
    {
        var options = new Dictionary<string, string> { ["build_property.RaskCqrsCodec"] = "true" };

        var run = GeneratorHarness.Run(
            Messages,
            new CqrsCodecGenerator(),
            options,
            "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Wire", "Microsoft.Extensions.DependencyInjection.Abstractions");
        var generated = string.Concat(run.RunResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText));

        Assert.Contains("Orders.Ship", generated, StringComparison.Ordinal);
        Assert.Contains("Orders.Returns.Refund", generated, StringComparison.Ordinal);
    }
}
