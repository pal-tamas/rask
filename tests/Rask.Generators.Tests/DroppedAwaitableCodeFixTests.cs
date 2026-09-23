using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

public class DroppedAwaitableCodeFixTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        public readonly struct Sending
        {
            public Sending In(TimeSpan delay) => this;
            public TaskAwaiter GetAwaiter() => Task.CompletedTask.GetAwaiter();
        }

        public static class Mail
        {
            public static Sending Send(string to) => default;
        }

        public static class Program
        {
            public static void Main() { }

            static void Register()
            {
                Mail.Send("ann@x.io").In(TimeSpan.FromHours(24));
            }
        }
        """;

    [Fact]
    public async Task Awaiting_it_also_makes_the_method_async_and_Task_returning()
    {
        var fixedSource = await CodeFixHarness.ApplyNamedAnalyzerFixAsync(
            new DroppedAwaitableAnalyzer(), new DroppedAwaitableCodeFixProvider(), "RASK093", Source, "RASK093_await");

        Assert.Contains(
            "await Mail.Send(\"ann@x.io\").In(TimeSpan.FromHours(24));", fixedSource, StringComparison.Ordinal);

        // async void would swallow the exception and take the process down, so a void method gains Task.
        Assert.Contains("static async Task Register()", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("async void", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discarding_it_writes_the_intent_down_rather_than_silencing_the_rule()
    {
        var fixedSource = await CodeFixHarness.ApplyNamedAnalyzerFixAsync(
            new DroppedAwaitableAnalyzer(), new DroppedAwaitableCodeFixProvider(), "RASK093", Source, "RASK093_discard");

        Assert.Contains(
            "_ = Mail.Send(\"ann@x.io\").In(TimeSpan.FromHours(24));", fixedSource, StringComparison.Ordinal);
        Assert.Contains("static void Register()", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_intentions_are_offered_because_either_can_be_the_right_one()
    {
        Assert.True(await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new DroppedAwaitableAnalyzer(), new DroppedAwaitableCodeFixProvider(), "RASK093", Source));
    }
}
