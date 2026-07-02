namespace Rask.Cqrs.Generators.Tests;

public sealed class CqrsDispatchGeneratorTests
{
    private const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using Rask.Cqrs;
        namespace Demo;
        """;

    [Fact]
    public void Emits_registry_and_invoker_for_a_query_handler()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record GetValue(int Id) : IQuery<string>;
            public sealed class GetValueHandler : IQueryHandler<GetValue, string>
            {
                public Task<string> HandleAsync(GetValue query, CancellationToken ct) => Task.FromResult("v");
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source);
        Assert.Contains("RegisterRequest(typeof(global::Demo.GetValue)", source);
        Assert.Contains("DynamicDependency", source);
        Assert.Contains("typeof(global::Demo.GetValueHandler)", source);
        Assert.Contains("global::Rask.Cqrs.IQueryHandler<global::Demo.GetValue, string>", source);
    }

    [Fact]
    public void Emits_unit_result_for_a_void_command()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record DoIt : ICommand;
            public sealed class DoItHandler : ICommandHandler<DoIt>
            {
                public Task HandleAsync(DoIt command, CancellationToken ct) => Task.CompletedTask;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("global::Rask.Cqrs.Unit", source);
        Assert.Contains("RegisterRequest(typeof(global::Demo.DoIt)", source);
    }

    [Fact]
    public void Emits_notification_fanout()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record Ping : INotification;
            public sealed class PingA : INotificationHandler<Ping>
            {
                public Task HandleAsync(Ping n, CancellationToken ct) => Task.CompletedTask;
            }
            public sealed class PingB : INotificationHandler<Ping>
            {
                public Task HandleAsync(Ping n, CancellationToken ct) => Task.CompletedTask;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("RegisterNotification(typeof(global::Demo.Ping)", source);
        Assert.Contains("NotificationDispatch.PublishAll", source);
        Assert.Contains("TryAddEnumerable", source);
    }

    [Fact]
    public void RASK028_reports_ambiguous_handler()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record GetValue : IQuery<int>;
            public sealed class HandlerOne : IQueryHandler<GetValue, int>
            {
                public Task<int> HandleAsync(GetValue query, CancellationToken ct) => Task.FromResult(1);
            }
            public sealed class HandlerTwo : IQueryHandler<GetValue, int>
            {
                public Task<int> HandleAsync(GetValue query, CancellationToken ct) => Task.FromResult(2);
            }
            """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK028");
    }

    [Fact]
    public void RASK029_reports_handler_without_public_constructor()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record GetValue : IQuery<int>;
            public sealed class PrivateHandler : IQueryHandler<GetValue, int>
            {
                private PrivateHandler() { }
                public Task<int> HandleAsync(GetValue query, CancellationToken ct) => Task.FromResult(1);
            }
            """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK029");
    }

    [Fact]
    public void Discovers_a_record_handler()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record GetValue(int Id) : IQuery<string>;
            public sealed record GetValueHandler : IQueryHandler<GetValue, string>
            {
                public Task<string> HandleAsync(GetValue query, CancellationToken ct) => Task.FromResult("v");
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("RegisterRequest(typeof(global::Demo.GetValue)", source);
        Assert.Contains("typeof(global::Demo.GetValueHandler)", source);
    }

    [Fact]
    public void Partial_handler_is_not_reported_as_ambiguous()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record GetValue : IQuery<int>;
            public partial class SplitHandler : IQueryHandler<GetValue, int>
            {
                public Task<int> HandleAsync(GetValue query, CancellationToken ct) => Task.FromResult(1);
            }
            public partial class SplitHandler : System.IDisposable
            {
                public void Dispose() { }
            }
            """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK028");
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Emits_nothing_when_there_are_no_handlers()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record NotAHandler(int X);
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.RunResult.Results.SelectMany(r => r.GeneratedSources));
    }
}
