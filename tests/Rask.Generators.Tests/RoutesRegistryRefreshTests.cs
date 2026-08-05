namespace Rask.Generators.Tests;

/// <summary>
///     Routes are registered by a <c>[ModuleInitializer]</c>, which the runtime never re-runs after a
///     hot-reload apply — so editing a <c>[Route]</c> template under <c>dotnet watch</c> silently did
///     nothing until a restart. The generator now emits a re-invocable <c>RefreshAll()</c> that Rask's
///     hot-reload coordinator calls by name.
///     <para>
///         The subtlety is that a refresh must <em>replace</em>. <c>RouteRegistry.Add</c> appends, and
///         every assembly with routed pages calls it, so re-running the initializer would have doubled
///         this assembly's routes — while clearing the registry first would have dropped the other
///         assemblies' routes and the default 404 fallback. Hence the keyed
///         <c>Replace(typeof(__RaskRoutesRegistry), …)</c>.
///     </para>
/// </summary>
public class RoutesRegistryRefreshTests
{
    // Must match the entry in RaskHotReload.RefreshTargetTypeNames — the coordinator resolves it with
    // Assembly.GetType(name), and this class is emitted into the global namespace.
    private const string RefreshTargetTypeName = "__RaskRoutesRegistry";

    private const string TwoPages = """
        using Rask.Core;
        using Rask.Core.Routing;
        namespace Demo;

        [Route("/")]
        public sealed class HomePage : Component
        {
            protected override Component? Render() => Div();
        }

        [Route("/about")]
        public sealed class AboutPage : Component
        {
            protected override Component? Render() => Div();
        }
        """;

    private static string Registry(string source) =>
        GeneratorDriverFixture.RunRoutes(source).GeneratedSource(RefreshTargetTypeName);

    [Fact]
    public void Init_delegates_to_a_reinvocable_RefreshAll()
    {
        var source = Registry(TwoPages);

        Assert.Contains(
            "[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("internal static void Init() => RefreshAll();", source, StringComparison.Ordinal);
        Assert.Contains("internal static void RefreshAll()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_replaces_this_assemblys_group_rather_than_appending()
    {
        var source = Registry(TwoPages);

        // Keyed on the registry class itself, so a refresh swaps only this assembly's contribution.
        Assert.Contains(
            "RouteRegistry.Replace(typeof(__RaskRoutesRegistry), new global::Rask.Core.Routing.RouteRegistration[]",
            source,
            StringComparison.Ordinal);

        // Add() appends — using it here is the duplicate-routes bug.
        Assert.DoesNotContain("RouteRegistry.Add(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_carries_every_route()
    {
        var source = Registry(TwoPages);

        Assert.Contains("""new(typeof(global::Demo.HomePage), "/", null)""", source, StringComparison.Ordinal);
        Assert.Contains("""new(typeof(global::Demo.AboutPage), "/about", null)""", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_is_internal_static_so_the_coordinator_can_reflect_onto_it()
    {
        // Looked up with BindingFlags.Static | NonPublic | Public; a private or instance member would
        // be found by nothing and fail silently.
        Assert.Contains("internal static void RefreshAll()", Registry(TwoPages), StringComparison.Ordinal);
    }

    [Fact]
    public void The_emitted_class_matches_the_name_the_coordinator_looks_for()
    {
        Assert.Contains(
            $"internal static class {RefreshTargetTypeName}", Registry(TwoPages), StringComparison.Ordinal);
    }

    [Fact]
    public void The_trimmer_annotations_stay_on_the_class_not_inside_RefreshAll()
    {
        // [DynamicDependency] must remain attached to the type so the trimmer roots page ctors and
        // properties; moving the body into RefreshAll must not have swept them along.
        var source = Registry(TwoPages);
        var initAt = source.IndexOf("internal static void Init()", StringComparison.Ordinal);

        Assert.Contains("DynamicDependency", source[..initAt], StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicDependency", source[initAt..], StringComparison.Ordinal);
    }
}
