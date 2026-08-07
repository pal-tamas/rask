namespace Rask.Jobs.Generators.Tests;

/// <summary>
/// Drives <see cref="JobRegistryGenerator"/> over hand-written sources.
/// </summary>
/// <remarks>
/// The registry key and the emitted <c>typeof(...)</c> operand are deliberately different strings — the
/// key is a runtime metadata name (unescaped, dotted), the operand is C# syntax (<c>global::</c>-prefixed,
/// <c>@</c>-escaped). Deriving one from the other is what made a job in a namespace like <c>@event</c>
/// register under a name the runtime never produces, so it silently dead-lettered. Most of what follows
/// pins that split down. Kept in lockstep with the outbox suite — the two generators share a base.
/// </remarks>
public class JobRegistryGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(source, new JobRegistryGenerator(), "Rask.Jobs", "Rask.Cqrs");

    [Fact]
    public void A_top_level_job_is_registered_under_its_full_name()
    {
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public sealed record SendWelcomeEmail(int UserId) : IJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("__RaskJobsRegistry");
        Assert.Contains(
            """("Demo.SendWelcomeEmail", typeof(global::Demo.SendWelcomeEmail)),""",
            source,
            StringComparison.Ordinal);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_nested_in_a_non_generic_type_keys_with_dots_not_a_plus()
    {
        // Type.FullName uses '+' between nesting levels and the serializer normalizes it to '.', so the
        // generated key has to be dotted to match.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public static class OrderJobs
            {
                public sealed record RequestReview(int OrderId) : IJob;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            """("Demo.OrderJobs.RequestReview", typeof(global::Demo.OrderJobs.RequestReview)),""",
            run.GeneratedSource("__RaskJobsRegistry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_in_a_keyword_namespace_keys_unescaped_but_is_referenced_escaped()
    {
        // The regression: ToDisplayString() escapes keyword identifiers ("Demo.@event.RequestReview"),
        // Type.FullName does not ("Demo.event.RequestReview"). Using one string for both roles meant the
        // key never matched, so the job failed to deserialize and burned attempts until it dead-lettered.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo.@event;
            public sealed record RequestReview(int OrderId) : IJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());

        var source = run.GeneratedSource("__RaskJobsRegistry");
        Assert.Contains(
            """("Demo.event.RequestReview", typeof(global::Demo.@event.RequestReview)),""",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"Demo.@event", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_whose_own_name_is_a_keyword_keys_unescaped()
    {
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public sealed record @class(int Id) : IJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            """("Demo.class", typeof(global::Demo.@class)),""",
            run.GeneratedSource("__RaskJobsRegistry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_nested_in_a_generic_type_is_skipped_with_RASK035()
    {
        // Skipping this was never broken — INamedTypeSymbol.IsGenericType is true when the type *or any
        // containing type* has type parameters, so the original guard already caught it. What's new is
        // that it now says so out loud instead of vanishing silently, and names the generic outer.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public class Outer<T>
            {
                public sealed record RequestReview(int OrderId) : IJob;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskJobsRegistry"));

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK035");
        Assert.Contains("nested inside the generic type 'Demo.Outer'", diagnostic.GetMessage(), StringComparison.Ordinal);

        // RASK035 is a Warning that says production will break — the type is skipped, so a queued job of
        // it fails to deserialize and dead-letters. Announcing that and not how to avoid it is the worst
        // shape a diagnostic has, so the remedy is part of the contract (#608).
        Assert.Contains(" — ", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("move it out of 'Demo.Outer'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <inheritdoc cref="A_job_nested_in_a_generic_type_is_skipped_with_RASK035" />
    [Fact]
    public void RASK035_on_a_file_local_job_says_how_to_fix_it()
    {
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            file sealed record RequestReview(int OrderId) : IJob;
            """);

        var message = Assert.Single(run.Diagnostics, d => d.Id == "RASK035").GetMessage();
        Assert.Contains("file-local", message, StringComparison.Ordinal);
        Assert.Contains(" — ", message, StringComparison.Ordinal);
        Assert.Contains("remove the 'file' modifier", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_in_the_global_namespace_keys_without_a_namespace()
    {
        var run = Run("""
            using Rask.Jobs;
            public sealed record RequestReview(int OrderId) : IJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            """("RequestReview", typeof(global::RequestReview)),""",
            run.GeneratedSource("__RaskJobsRegistry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_local_job_is_skipped_with_RASK035()
    {
        // Regression: this used to be registered under "Demo.RequestReview", but a file-local type's
        // runtime FullName carries a synthesized "<file>F0__" segment, so the key could never match and
        // the job dead-lettered.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            file sealed record RequestReview(int OrderId) : IJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskJobsRegistry"));
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK035");
    }

    [Fact]
    public void An_inaccessible_nested_job_is_skipped_with_RASK035()
    {
        // Regression: this used to emit typeof(global::Demo.Outer.RequestReview) for a private type,
        // which fails to compile with CS0122 — the whole assembly stopped building.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public class Outer
            {
                private sealed record RequestReview(int OrderId) : IJob;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskJobsRegistry"));
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK035");
    }

    [Fact]
    public void An_abstract_base_is_skipped_silently_and_its_concrete_derivative_registered()
    {
        // Modelling a hierarchy with an abstract base that carries the marker is normal, not a mistake:
        // skip it, but don't nag about it.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public abstract record MaintenanceJob : IJob;
            public sealed record PurgeCancelled(int Days) : MaintenanceJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK035");

        var source = run.GeneratedSource("__RaskJobsRegistry");
        Assert.Contains("""("Demo.PurgeCancelled", """, source, StringComparison.Ordinal);
        Assert.DoesNotContain("Demo.MaintenanceJob\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generic_job_is_skipped_with_RASK035()
    {
        // A closed generic's FullName carries assembly-qualified type arguments, so no static key matches.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public sealed record Reindex<T>(int Id) : IJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskJobsRegistry"));
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK035");
    }

    [Fact]
    public void A_job_split_across_partials_is_registered_once()
    {
        // Each partial declaration carrying the base list is visited separately.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public partial record RequestReview : IJob;
            public partial record RequestReview : IJob;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Equal(1, CountOccurrences(run.GeneratedSource("__RaskJobsRegistry"), "(\"Demo.RequestReview\", "));
    }

    [Fact]
    public void A_compilation_with_no_jobs_generates_nothing()
    {
        var run = Run("""
            namespace Demo;
            public sealed record NotAJob(int Id);
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource("__RaskJobsRegistry"));
    }

    [Fact]
    public void Registrations_are_emitted_in_ordinal_order()
    {
        // Deterministic output keeps the incremental cache stable across unrelated edits.
        var run = Run("""
            using Rask.Jobs;
            namespace Demo;
            public sealed record Zulu(int Id) : IJob;
            public sealed record Alpha(int Id) : IJob;
            """);

        var source = run.GeneratedSource("__RaskJobsRegistry");
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
