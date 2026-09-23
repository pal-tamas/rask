using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class DroppedAwaitableAnalyzerTests
{
    // A builder shaped exactly like Rask's own: a readonly struct that does nothing until it is awaited,
    // whose steps hand back a NEW one. Declared in the test source rather than referenced, because the
    // analyzer tests the shape, not a list of names — which is the thing worth pinning.
    private static string Program(string body) => $$"""
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
            public static Task SendNow(string to) => Task.CompletedTask;
            public static void Log(string to) { }
        }

        // The shape every event prop has: a lambda binds to Action unless it produces a Task.
        public static class El
        {
            public static void OnClick(Action? value) { }
            public static void OnClick(Func<Task>? value) { }
        }

        public static class Program
        {
            public static void Main() { }
            {{body}}
        }
        """;

    [Fact]
    public async Task A_builder_dropped_in_a_sync_method_is_reported_as_RASK093()
    {
        var d = Assert.Single(await Diagnostics(Program(
            """
            static void Register()
            {
                Mail.Send("ann@x.io");
            }
            """)));

        Assert.Equal("RASK093", d.Id);
        Assert.Contains("does nothing until it is awaited", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Mail.Send(\"ann@x.io\")", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_builder_dropped_after_a_step_is_reported_too()
    {
        // The quieter half: `.In(…)` hands back a NEW builder, so dropping the result loses the delay
        // as surely as dropping the send loses the mail.
        var d = Assert.Single(await Diagnostics(Program(
            """
            static void Register()
            {
                Mail.Send("ann@x.io").In(TimeSpan.FromHours(24));
            }
            """)));

        Assert.Equal("RASK093", d.Id);
    }

    [Fact]
    public async Task A_dropped_Task_in_a_sync_method_is_reported()
    {
        // Not only Rask's builders: a Task dropped where nothing can observe it is the same bug, and
        // the compiler is silent about it outside an async method.
        Assert.Single(await Diagnostics(Program(
            """
            static void Register()
            {
                Mail.SendNow("ann@x.io");
            }
            """)));
    }

    [Fact]
    public async Task An_awaited_builder_reports_nothing()
    {
        Assert.Empty(await Diagnostics(Program(
            """
            static async Task Register()
            {
                await Mail.Send("ann@x.io").In(TimeSpan.FromHours(24));
            }
            """)));
    }

    [Fact]
    public async Task A_builder_dropped_in_an_async_method_is_left_to_the_compiler()
    {
        // CS4014 already refuses this, as an error under -warnaserror. Reporting it again would put two
        // messages on one line saying the same thing.
        Assert.Empty(await Diagnostics(Program(
            """
            static async Task Register()
            {
                Mail.Send("ann@x.io");
                await Task.CompletedTask;
            }
            """)));
    }

    [Fact]
    public async Task A_builder_held_in_a_variable_reports_nothing()
    {
        // Assigning it keeps the value, so awaiting it later is a legitimate shape and must stay quiet.
        Assert.Empty(await Diagnostics(Program(
            """
            static async Task Register()
            {
                var sending = Mail.Send("ann@x.io").In(TimeSpan.FromHours(24));
                await sending;
            }
            """)));
    }

    [Fact]
    public async Task A_call_that_returns_nothing_reports_nothing()
    {
        Assert.Empty(await Diagnostics(Program(
            """
            static void Register()
            {
                Mail.Log("ann@x.io");
            }
            """)));
    }

    [Fact]
    public async Task A_sync_local_function_inside_an_async_method_is_still_reported()
    {
        // The nearest enclosing function decides, not the outermost: a sync local function inside an
        // async method is where CS4014 stops looking, and so is exactly where this slips through.
        Assert.Single(await Diagnostics(Program(
            """
            static async Task Register()
            {
                Inner();
                await Task.CompletedTask;

                static void Inner() => Mail.Send("ann@x.io");
            }
            """)));
    }

    [Fact]
    public async Task A_builder_in_a_lambda_bound_to_a_void_delegate_is_reported()
    {
        // The worst of the three shapes, and the only one with no compiler warning at all: OnClick
        // offers Action and Func<Task>, `() => Mail.Send(…)` binds to Action, and the send is discarded.
        // Nothing is sent, nothing is logged, and the line looks exactly like the one that works.
        var d = Assert.Single(await Diagnostics(Program(
            """
            static void Wire()
            {
                El.OnClick(() => Mail.Send("ann@x.io"));
            }
            """)));

        Assert.Equal("RASK093", d.Id);
        Assert.Contains("Mail.Send(\"ann@x.io\")", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lambda_that_hands_back_the_task_reports_nothing()
    {
        // Same call site, written so the task reaches the caller: binds to Func<Task>, awaited there.
        Assert.Empty(await Diagnostics(Program(
            """
            static void Wire()
            {
                El.OnClick(() => Mail.SendNow("ann@x.io"));
            }
            """)));
    }

    [Fact]
    public async Task An_async_lambda_reports_nothing()
    {
        Assert.Empty(await Diagnostics(Program(
            """
            static void Wire()
            {
                El.OnClick(async () => await Mail.Send("ann@x.io"));
            }
            """)));
    }

    [Fact]
    public async Task A_lambda_whose_body_is_not_awaitable_reports_nothing()
    {
        Assert.Empty(await Diagnostics(Program(
            """
            static void Wire()
            {
                El.OnClick(() => Mail.Log("ann@x.io"));
            }
            """)));
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestProgram",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DroppedAwaitableAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK093").ToImmutableArray();
    }
}
