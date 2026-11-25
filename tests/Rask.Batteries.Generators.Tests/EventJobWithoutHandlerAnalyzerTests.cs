using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK189: <c>b.Job&lt;E&gt;()</c> on an aggregate that has no <c>static Task On(E e)</c> for the job to run.</summary>
public class EventJobWithoutHandlerAnalyzerTests
{
    private static string Source(string handler) => $$"""
        using System;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Background;
        using Rask.Cqrs;
        using Rask.Data;
        namespace Loads;
        public sealed record FileQueued(Guid File) : IEvent;
        public sealed partial class QueuedFile : Aggregate<Guid>
        {
            {{handler}}

            public static void Configure(EntityTypeBuilder<QueuedFile> b) =>
                b.Job<FileQueued>().InTenantLane("loads").UnlessWaiting(e => e.File.ToString());
        }
        """;

    [Fact]
    public async Task A_job_for_an_event_the_aggregate_does_not_handle_is_RASK189()
    {
        var source = Source(handler: "");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new EventJobWithoutHandlerAnalyzer(), source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("RASK189", diagnostic.Id);
        Assert.Contains("'QueuedFile' declares Job<FileQueued>()", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_job_for_the_event_the_aggregates_On_takes_is_quiet()
    {
        var source = Source(handler: "static Task On(FileQueued e) => Task.CompletedTask;");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new EventJobWithoutHandlerAnalyzer(), source);

        Assert.Empty(diagnostics);
    }
}
