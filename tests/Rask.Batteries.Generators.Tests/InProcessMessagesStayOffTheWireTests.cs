using Rask.Cqrs;

namespace Rask.Batteries.Generators.Tests;

// A job payload and an outbox event are only ever sent by the server to itself. With a codec, anyone could
// POST one to /_rask/cqrs and run its handler at once — a welcome-mail job becomes an open mail relay.
public sealed class InProcessMessagesStayOffTheWireTests
{
    [Fact]
    public void A_job_and_an_outbox_event_get_no_wire_contract_while_an_ordinary_command_does()
    {
        var source = """
            using System.Threading.Tasks;
            using Rask.Cqrs;
            namespace Demo;

            public sealed record SendWelcomeEmail(string Email) : Rask.Jobs.IJob;
            public sealed class SendWelcomeEmailHandler : ICommandHandler<SendWelcomeEmail>
            {
                public Task Handle(SendWelcomeEmail c) => Task.CompletedTask;
            }

            public sealed record OrderPaid(int OrderId) : Rask.Outbox.IOutboxEvent;
            public sealed class OrderPaidHandler : INotificationHandler<OrderPaid>
            {
                public Task Handle(OrderPaid n) => Task.CompletedTask;
            }

            public sealed record RenameTodo(int Id, string Title) : ICommand;
            public sealed class RenameTodoHandler : ICommandHandler<RenameTodo>
            {
                public Task Handle(RenameTodo c) => Task.CompletedTask;
            }
            """;

        var run = GeneratorHarness.Run(source, new CqrsCodecGenerator(), "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Wire");

        Assert.Empty(run.GeneratedCompileErrors());
        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("typeof(global::Demo.RenameTodo)", codecs, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Demo.SendWelcomeEmail", codecs, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Demo.OrderPaid", codecs, StringComparison.Ordinal);
    }

    [Fact]
    public void The_auth_events_are_marked_local_only_so_no_browser_can_publish_or_watch_them()
    {
        var events = typeof(Rask.Auth.UserRegistered).Assembly.GetTypes()
            .Where(t => typeof(INotification).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();

        var exposed = events.Where(t => !t.IsDefined(typeof(LocalOnlyAttribute), inherit: false)).Select(t => t.Name);

        Assert.NotEmpty(events);
        Assert.Empty(exposed);
    }
}
