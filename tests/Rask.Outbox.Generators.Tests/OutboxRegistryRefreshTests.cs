namespace Rask.Outbox.Generators.Tests;

/// <summary>
///     The registry is populated by a <c>[ModuleInitializer]</c>, which the runtime never re-runs after
///     a hot-reload apply — so adding or renaming an event under <c>dotnet watch</c> used to silently do
///     nothing until a restart. The generator now emits a re-invocable <c>RefreshAll()</c> that Rask's
///     hot-reload coordinator calls by name.
///     <para>
///         Kept in lockstep with the jobs suite — the two generators share
///         <c>RegistryGeneratorBase</c>, and its own doc comment notes they have drifted into the same
///         bug together before.
///     </para>
/// </summary>
public class OutboxRegistryRefreshTests
{
    // Must match the entry in RaskHotReload.RefreshTargetTypeNames. The coordinator resolves this
    // with Assembly.GetType(name), so both the namespace and the class name are load-bearing.
    private const string RefreshTargetTypeName = "Rask.Outbox.Generated.__RaskOutboxRegistry";

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(source, new OutboxRegistryGenerator(), "Rask.Outbox", "Rask.Cqrs");

    private const string OneEvent = """
        using Rask.Outbox;
        namespace Demo;
        public sealed record OrderPlaced(int OrderId) : IOutboxEvent;
        """;

    [Fact]
    public void Init_delegates_to_a_reinvocable_RefreshAll()
    {
        var run = Run(OneEvent);
        var source = run.GeneratedSource("__RaskOutboxRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("internal static void Init() => RefreshAll();", source, StringComparison.Ordinal);
        Assert.Contains("internal static void RefreshAll()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_carries_the_registrations_not_Init()
    {
        var run = Run(OneEvent);
        var source = run.GeneratedSource("__RaskOutboxRegistry");

        // Init must be the one-line delegation — anything registered directly in its body would not
        // be reachable from a refresh.
        var init = source[source.IndexOf("void Init()", StringComparison.Ordinal)..];
        var refreshAt = init.IndexOf("RefreshAll()\n", StringComparison.Ordinal);
        Assert.DoesNotContain("Replace(", init[..Math.Max(refreshAt, 0)], StringComparison.Ordinal);

        var refresh = source[source.IndexOf("void RefreshAll()", StringComparison.Ordinal)..];
        Assert.Contains("(\"Demo.OrderPlaced\", ", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_replaces_this_assemblys_whole_set_rather_than_upserting()
    {
        // #537. A run of upserts made a rename additive: the old name kept resolving to a type the
        // generator no longer produced until the process restarted. One Replace call, keyed on this
        // assembly's own registry class, swaps the whole contribution and leaves other assemblies alone.
        var source = Run(OneEvent).GeneratedSource("__RaskOutboxRegistry");

        Assert.Contains(
            "global::Rask.Outbox.OutboxSerializerRegistry.Replace(typeof(__RaskOutboxRegistry), ",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterEvent(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_is_internal_static_so_the_coordinator_can_reflect_onto_it()
    {
        // The coordinator looks it up with BindingFlags.Static | NonPublic | Public. A private or
        // instance member would be found by nothing and fail silently.
        Assert.Contains(
            "internal static void RefreshAll()",
            Run(OneEvent).GeneratedSource("__RaskOutboxRegistry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_emitted_class_matches_the_name_the_coordinator_looks_for()
    {
        var source = Run(OneEvent).GeneratedSource("__RaskOutboxRegistry");
        var lastDot = RefreshTargetTypeName.LastIndexOf('.');

        Assert.Contains($"namespace {RefreshTargetTypeName[..lastDot]}", source, StringComparison.Ordinal);
        Assert.Contains(
            $"internal static class {RefreshTargetTypeName[(lastDot + 1)..]}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_events_still_emits_no_registry()
    {
        // Refresh must not start emitting an empty class into every assembly that references Rask.Outbox.
        var run = Run("namespace Demo; public sealed record NotAnEvent(int X);");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskOutboxRegistry"));
    }
}
