using System.Globalization;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     An aggregate's <c>static Task On(E e)</c> is a durable handler of <c>E</c>: the generator registers it from the
///     aggregate's own partial class (so <c>On</c> stays private), and refuses an <c>On</c> of the wrong shape (RASK179) or
///     on an aggregate it cannot add to (RASK188).
/// </summary>
public class AggregateEventHandlerGeneratorTests
{
    private const string Fuelings = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Rask.Cqrs;
        using Rask.Data;
        namespace Fuel;

        public sealed record DriverChipsChanged(IReadOnlyList<string> Codes) : IEvent;

        public sealed record VehicleChipsChanged(IReadOnlyList<string> Codes) : IEvent;

        public sealed partial class Fueling : Aggregate<int>
        {
            public string DriverChip { get; private set; } = "";

            static Task On(DriverChipsChanged e) => Task.CompletedTask;
        }
        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(source, [new AggregateEventHandlerGenerator()], "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    private static IEnumerable<string> Messages(GeneratorRun run, string id) =>
        run.Diagnostics.Where(d => d.Id == id).Select(static d => d.GetMessage(CultureInfo.InvariantCulture));

    [Fact]
    public void A_static_On_of_an_event_is_registered_as_the_aggregates_durable_handler_of_it()
    {
        var run = Run(Fuelings);

        var registration = run.GeneratedSource("Fuel.Fueling.On");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("partial class Fueling", registration, StringComparison.Ordinal);
        Assert.Contains("ReplaceDurableHandlers(typeof(global::Fuel.Fueling)", registration, StringComparison.Ordinal);
        Assert.Contains("(typeof(global::Fuel.DriverChipsChanged), typeof(global::Fuel.Fueling),", registration, StringComparison.Ordinal);
        Assert.Contains(
            "AggregateEvents.Handle<global::Fuel.Fueling, global::Fuel.DriverChipsChanged>(provider, (global::Fuel.DriverChipsChanged)e, static ev => On(ev), ct)",
            registration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_event_the_aggregate_handles_is_one_registration_of_the_same_class()
    {
        var run = Run(Fuelings.Replace(
            "static Task On(DriverChipsChanged e) => Task.CompletedTask;",
            "static Task On(DriverChipsChanged e) => Task.CompletedTask;\n    static Task On(VehicleChipsChanged e) => Task.CompletedTask;",
            StringComparison.Ordinal));

        var registration = run.GeneratedSource("Fuel.Fueling.On");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("typeof(global::Fuel.DriverChipsChanged)", registration, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Fuel.VehicleChipsChanged)", registration, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Task On(DriverChipsChanged e) => Task.CompletedTask;", "is not static")]
    [InlineData("static void On(DriverChipsChanged e) { }", "returns 'void', not Task")]
    [InlineData("static Task On(string code) => Task.CompletedTask;", "takes 'string', which is not an IEvent")]
    [InlineData("static Task On(DriverChipsChanged e, int n) => Task.CompletedTask;", "takes 2 parameters, not one event")]
    public void An_On_of_another_shape_is_refused_saying_what_it_is(string declaration, string refusal)
    {
        var run = Run(Fuelings.Replace("static Task On(DriverChipsChanged e) => Task.CompletedTask;", declaration, StringComparison.Ordinal));

        var messages = Messages(run, "RASK179");

        Assert.Contains(messages, m => m.Contains(refusal, StringComparison.Ordinal));
        Assert.False(run.HasGeneratedSource("Fuel.Fueling.On"));
    }

    [Fact]
    public void An_aggregate_that_is_not_partial_is_refused_naming_the_event()
    {
        var run = Run(Fuelings.Replace("sealed partial class Fueling", "sealed class Fueling", StringComparison.Ordinal));

        var message = Assert.Single(Messages(run, "RASK188"));

        Assert.Contains("'Fueling' handles 'Fuel.DriverChipsChanged' in On, but it is not partial", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_named_On_outside_an_aggregate_is_left_alone()
    {
        var run = Run(Fuelings.Replace("public sealed partial class Fueling : Aggregate<int>", "public sealed partial class Fueling", StringComparison.Ordinal));

        var generated = run.HasGeneratedSource("Fuel.Fueling.On");

        Assert.False(generated);
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id is "RASK179" or "RASK188");
    }
}
