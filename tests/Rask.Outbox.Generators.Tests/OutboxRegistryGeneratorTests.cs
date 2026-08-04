namespace Rask.Outbox.Generators.Tests;

/// <summary>
/// Drives <see cref="OutboxRegistryGenerator"/> over hand-written sources.
/// </summary>
/// <remarks>
/// The registry key and the emitted <c>typeof(...)</c> operand are deliberately different strings — the
/// key is a runtime metadata name (unescaped, dotted), the operand is C# syntax (<c>global::</c>-prefixed,
/// <c>@</c>-escaped). Deriving one from the other is what made an event in a namespace like <c>@event</c>
/// register under a name the runtime never produces, so it silently dead-lettered. Most of what follows
/// pins that split down.
/// </remarks>
public class OutboxRegistryGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(source, new OutboxRegistryGenerator(), "Rask.Outbox", "Rask.Cqrs");

    [Fact]
    public void A_top_level_event_is_registered_under_its_full_name()
    {
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public sealed record OrderPlaced(int Id) : IOutboxEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("__RaskOutboxRegistry");
        Assert.Contains(
            """RegisterEvent("Demo.OrderPlaced", typeof(global::Demo.OrderPlaced));""",
            source,
            StringComparison.Ordinal);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_nested_in_a_non_generic_type_keys_with_dots_not_a_plus()
    {
        // Type.FullName uses '+' between nesting levels and the serializer normalizes it to '.', so the
        // generated key has to be dotted to match.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public static class OrderEvents
            {
                public sealed record Placed(int Id) : IOutboxEvent;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            """RegisterEvent("Demo.OrderEvents.Placed", typeof(global::Demo.OrderEvents.Placed));""",
            run.GeneratedSource("__RaskOutboxRegistry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_in_a_keyword_namespace_keys_unescaped_but_is_referenced_escaped()
    {
        // The regression: ToDisplayString() escapes keyword identifiers ("Demo.@event.Ev"), Type.FullName
        // does not ("Demo.event.Ev"). Using one string for both roles meant the key never matched.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo.@event;
            public sealed record Raised(int Id) : IOutboxEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("__RaskOutboxRegistry");
        Assert.Contains(
            """RegisterEvent("Demo.event.Raised", typeof(global::Demo.@event.Raised));""",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"Demo.@event", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_whose_own_name_is_a_keyword_keys_unescaped()
    {
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public sealed record @class(int Id) : IOutboxEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            """RegisterEvent("Demo.class", typeof(global::Demo.@class));""",
            run.GeneratedSource("__RaskOutboxRegistry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_nested_in_a_generic_type_is_skipped_with_RASK035()
    {
        // Skipping this was never broken — INamedTypeSymbol.IsGenericType is true when the type *or any
        // containing type* has type parameters, so the original guard already caught it. What's new is
        // that it now says so out loud instead of vanishing silently, and names the generic outer.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public class Outer<T>
            {
                public sealed record Raised(int Id) : IOutboxEvent;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskOutboxRegistry"));

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK035");
        Assert.Contains("nested inside the generic type 'Demo.Outer'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_in_the_global_namespace_keys_without_a_namespace()
    {
        var run = Run("""
            using Rask.Outbox;
            public sealed record Raised(int Id) : IOutboxEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            """RegisterEvent("Raised", typeof(global::Raised));""",
            run.GeneratedSource("__RaskOutboxRegistry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_local_event_is_skipped_with_RASK035()
    {
        // Regression: this used to be registered under "Demo.Raised", but a file-local type's runtime
        // FullName carries a synthesized "<file>F0__" segment, so the key could never match and the
        // event dead-lettered.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            file sealed record Raised(int Id) : IOutboxEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskOutboxRegistry"));
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK035");
    }

    [Fact]
    public void An_inaccessible_nested_event_is_skipped_with_RASK035()
    {
        // Regression: this used to emit typeof(global::Demo.Outer.Raised) for a private type, which fails
        // to compile with CS0122 — the whole assembly stopped building.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public class Outer
            {
                private sealed record Raised(int Id) : IOutboxEvent;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskOutboxRegistry"));
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK035");
    }

    [Fact]
    public void An_abstract_base_is_skipped_silently_and_its_concrete_derivative_registered()
    {
        // Modelling a hierarchy with an abstract base that carries the marker is normal, not a mistake:
        // skip it, but don't nag about it.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public abstract record OrderEvent : IOutboxEvent;
            public sealed record Placed(int Id) : OrderEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK035");

        var source = run.GeneratedSource("__RaskOutboxRegistry");
        Assert.Contains("""RegisterEvent("Demo.Placed", """, source, StringComparison.Ordinal);
        Assert.DoesNotContain("Demo.OrderEvent\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generic_event_is_skipped_with_RASK035()
    {
        // A closed generic's FullName carries assembly-qualified type arguments, so no static key matches.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public sealed record Raised<T>(T Payload) : IOutboxEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskOutboxRegistry"));
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK035");
    }

    [Fact]
    public void An_event_split_across_partials_is_registered_once()
    {
        // Each partial declaration carrying the base list is visited separately.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public partial record Raised : IOutboxEvent;
            public partial record Raised : IOutboxEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Equal(1, CountOccurrences(run.GeneratedSource("__RaskOutboxRegistry"), "RegisterEvent("));
    }

    [Fact]
    public void A_compilation_with_no_events_generates_nothing()
    {
        var run = Run("""
            namespace Demo;
            public sealed record NotAnEvent(int Id);
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskOutboxRegistry"));
    }

    [Fact]
    public void Registrations_are_emitted_in_ordinal_order()
    {
        // Deterministic output keeps the incremental cache stable across unrelated edits.
        var run = Run("""
            using Rask.Outbox;
            namespace Demo;
            public sealed record Zulu(int Id) : IOutboxEvent;
            public sealed record Alpha(int Id) : IOutboxEvent;
            """);

        var source = run.GeneratedSource("__RaskOutboxRegistry");
        Assert.True(
            source.IndexOf("Demo.Alpha", StringComparison.Ordinal) <
            source.IndexOf("Demo.Zulu", StringComparison.Ordinal),
            "Registrations should be ordered ordinally by key.");
    }

    private static int CountOccurrences(string haystack, string needle)
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
