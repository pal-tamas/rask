using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;
using Rask.Jobs;

namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     A background job. <see cref="IBackgroundJob" /> is a <c>Rask.Cqrs</c> command, so this is exactly the
///     declaration you would write on a server — no browser-specific anything.
/// </summary>
public sealed record GreetJob(string Name) : IBackgroundJob;

/// <summary>
///     The handler, an ordinary <see cref="ICommandHandler{TCommand}" />. The job processor dispatches to
///     it from its poll loop, in a fresh DI scope, having claimed the row with a lease.
/// </summary>
/// <remarks>
///     It writes to the database and then raises <see cref="GreetingFeed" />, which is how a page finds
///     out. That is the interesting half of this sample: the work happens outside any click handler, and
///     the re-render it triggers is an out-of-band one.
/// </remarks>
public sealed class GreetJobHandler(IDbContextFactory<AppDbContext> factory, GreetingFeed feed)
    : ICommandHandler<GreetJob>
{
    public async Task HandleAsync(GreetJob command, CancellationToken cancellationToken)
    {
        // Deliberately slow enough to see: the button returns immediately and the row appears a moment
        // later, which is the difference between queueing work and doing it.
        await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken).ConfigureAwait(false);

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Greetings.Add(new Greeting { Text = $"Hello, {command.Name}!", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        feed.Changed();
    }
}
