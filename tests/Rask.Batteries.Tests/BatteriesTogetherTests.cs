using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Core;
using Rask.Cqrs;
using Rask.Data;
using Rask.Mailing;

namespace Rask.Batteries.Tests;

public sealed record OrderPlaced(string Customer) : IEvent;

public sealed record SendReceipt(string Customer) : IJob;

/// <summary>The outbox's part: hands the slow work to the job queue, through the facade.</summary>
public sealed class QueueReceipt : IDurableHandler<OrderPlaced>
{
    public async Task Handle(OrderPlaced e) => await Jobs.Enqueue(new SendReceipt(e.Customer));
}

/// <summary>The job's part: sends the mail, through the facade.</summary>
public sealed class SendReceiptHandler : ICommandHandler<SendReceipt>
{
    public async Task Handle(SendReceipt command) =>
        await Mail.Send(Email.To(command.Customer).Subject("Your receipt").Html("<p>Thank you.</p>"));
}

/// <summary>The smallest root a <see cref="RaskApp" /> can serve.</summary>
public sealed partial class TogetherApp : Component
{
    protected override Component? Render() => H1["ok"];
}

/// <summary>Stands in for SMTP, and records who each email was sent for.</summary>
public sealed class ReceiptInbox : IMailSender
{
    public ConcurrentQueue<(string To, Guid? SentFor)> Received { get; } = [];

    public Task Send(OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        Received.Enqueue((mail.To[0].Address, Current.UserId));
        return Task.CompletedTask;
    }
}

/// <summary>
///     One flow across the batteries, on a real <see cref="RaskApp" /> with its real processors: an event is
///     published, the outbox runs its durable handler, the handler enqueues a job, the job sends mail — each
///     step through a static facade with nothing injected, and each for the user who started it.
/// </summary>
public sealed class BatteriesTogetherTests
{
    [Fact]
    public async Task Publishing_an_order_mails_a_receipt_for_the_user_who_placed_it()
    {
        var alice = Guid.NewGuid();
        var inbox = new ReceiptInbox();
        var file = Path.Combine(Path.GetTempPath(), $"rask-together-{Guid.NewGuid():N}.db");
        var app = Built(file, inbox);
        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>()
                         .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using (var session = app.Services.CreateScope())
            using (Current.UseUser(alice))
            {
                await session.ServiceProvider.GetRequiredService<IDispatcher>()
                    .Publish(new OrderPlaced("ann@example.com"), TestContext.Current.CancellationToken);
            }

            await Until(() => !inbox.Received.IsEmpty);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            File.Delete(file);
        }

        Assert.Equal(("ann@example.com", alice), Assert.Single(inbox.Received));
    }

    private static Microsoft.AspNetCore.Builder.WebApplication Built(string file, ReceiptInbox inbox)
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Services.AddSingleton<IMailSender>(inbox);
        app.Configure(c =>
        {
            c.ConnectionString = $"Data Source={file};Pooling=False";
            c.MigrateOnStart = false;
            c.Jobs.Configure(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            c.Mail.Configure(o =>
            {
                o.From = "shop@example.com";
                o.PollInterval = TimeSpan.FromMilliseconds(20);
            });
            c.Outbox.Configure(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            c.Auth.Off();
            c.Cache.Off();
            c.Storage.Off();
            c.Push.Off();
            c.Ops.Off();
            c.Snapshots.Off();
            c.Logs.Off();
        });
        return app.Build<TogetherApp>();
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the receipt never arrived");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
