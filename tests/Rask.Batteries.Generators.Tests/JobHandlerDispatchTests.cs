namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// An <c>IJobHandler&lt;TJob&gt;</c> is found and registered exactly as an <c>ICommandHandler&lt;TJob&gt;</c> is, because
/// it is one underneath: the processor dispatches a job through <c>IDispatcher.Send</c>, and that looks up the
/// command handler.
/// </summary>
public sealed class JobHandlerDispatchTests
{
    private const string Preamble = """
        using System.Threading.Tasks;
        using Rask.Background;
        using Rask.Cqrs;
        namespace Demo;
        public sealed record SendReceipt(int OrderId) : IJob;
        """;

    [Fact]
    public void A_job_handler_is_registered_as_the_command_handler_of_its_job()
    {
        var run = Run(Preamble + """
            public sealed class SendReceiptHandler : IJobHandler<SendReceipt>
            {
                public Task Handle(SendReceipt job) => Task.CompletedTask;
            }
            """);

        var source = run.GeneratedSource("__RaskCqrsRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("(typeof(global::Demo.SendReceipt), __Request_", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.SendReceiptHandler)", source, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Cqrs.ICommandHandler<global::Demo.SendReceipt>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_handler_written_explicitly_still_compiles_with_its_registration()
    {
        var run = Run(Preamble + """
            public sealed class SendReceiptHandler : IJobHandler<SendReceipt>
            {
                Task IJobHandler<SendReceipt>.Handle(SendReceipt job) => Task.CompletedTask;
            }
            """);

        var source = run.GeneratedSource("__RaskCqrsRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("typeof(global::Demo.SendReceiptHandler)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_with_a_job_handler_and_a_command_handler_is_ambiguous()
    {
        var run = Run(Preamble + """
            public sealed class NewHandler : IJobHandler<SendReceipt>
            {
                public Task Handle(SendReceipt job) => Task.CompletedTask;
            }
            public sealed class OldHandler : ICommandHandler<SendReceipt>
            {
                public Task Handle(SendReceipt command) => Task.CompletedTask;
            }
            """);

        var ambiguous = run.Diagnostics.Where(d => d.Id == "RASK028");

        Assert.NotEmpty(ambiguous);
    }

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            new CqrsDispatchGenerator(),
            "Rask.Cqrs",
            "Rask.Jobs",
            "Microsoft.Extensions.DependencyInjection.Abstractions");
}
