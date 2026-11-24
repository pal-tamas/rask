using System.Globalization;

namespace Rask.Batteries.Generators.Tests;

/// <summary>An aggregate's <c>private void OnDelete()</c> is registered as its delete hook; another shape is RASK190.</summary>
public class DeleteHookGeneratorTests
{
    private const string Drivers = """
        using Rask.Cqrs;
        using Rask.Data;
        namespace Fleet;

        public sealed record DriverRemoved(int Id) : IEvent;

        public sealed partial class Driver : Aggregate<int>
        {
            private void OnDelete() => Raise(new DriverRemoved(Id));
        }
        """;

    private const string Hook = "private void OnDelete() => Raise(new DriverRemoved(Id));";

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(source, [new DeleteHookGenerator()], "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_private_OnDelete_is_registered_as_the_aggregates_delete_hook()
    {
        var run = Run(Drivers);

        var registration = run.GeneratedSource("Fleet.Driver.OnDelete");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "global::Rask.Data.DeleteHooks.Register(typeof(global::Fleet.Driver), static aggregate => ((global::Fleet.Driver)aggregate).OnDelete());",
            registration,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public void OnDelete() => Raise(new DriverRemoved(Id));", "is not private")]
    [InlineData("private static void OnDelete() { }", "is static")]
    [InlineData("private void OnDelete(int reason) { }", "takes parameters")]
    [InlineData("private int OnDelete() => 0;", "returns 'int', not void")]
    public void An_OnDelete_of_another_shape_is_refused_saying_what_it_is(string declaration, string refusal)
    {
        var run = Run(Drivers.Replace(Hook, declaration, StringComparison.Ordinal));

        var message = Assert.Single(run.Diagnostics, static d => d.Id == "RASK190").GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains(refusal, message, StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource("Fleet.Driver.OnDelete"));
    }

    [Fact]
    public void An_aggregate_that_is_not_partial_is_refused()
    {
        var run = Run(Drivers.Replace("sealed partial class Driver", "sealed class Driver", StringComparison.Ordinal));

        var message = Assert.Single(run.Diagnostics, static d => d.Id == "RASK190").GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("it is not partial", message, StringComparison.Ordinal);
    }
}
