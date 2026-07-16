using Microsoft.EntityFrameworkCore;

namespace Rask.Mail;

/// <summary>
/// The default <see cref="IMailQueue"/>: renders the email (already done by the builder) and writes one
/// <see cref="QueuedMail"/> row through the app's <see cref="IDbContextFactory{TContext}"/>. The sender is
/// resolved once, at enqueue time — from the message's own <c>From</c> if set, otherwise
/// <see cref="MailOptions.From"/> — so the stored row is self-contained.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the mail table.</typeparam>
public sealed class MailQueue<TContext>(IDbContextFactory<TContext> contextFactory, MailOptions options, TimeProvider timeProvider) : IMailQueue
    where TContext : DbContext
{
    /// <inheritdoc/>
    public Task SendAsync(Email email, CancellationToken cancellationToken = default) =>
        WriteAsync(email, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    /// <inheritdoc/>
    public Task ScheduleAsync(Email email, TimeSpan delay, CancellationToken cancellationToken = default) =>
        WriteAsync(email, timeProvider.GetUtcNow().UtcDateTime + delay, cancellationToken);

    /// <inheritdoc/>
    public Task ScheduleAsync(Email email, DateTimeOffset runAt, CancellationToken cancellationToken = default) =>
        WriteAsync(email, runAt.UtcDateTime, cancellationToken);

    private async Task WriteAsync(Email email, DateTime runAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var from = email.FromAddress ?? new EmailAddress(options.From, options.FromName);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var message = MailSerializer.ToQueuedMail(email, from, runAt, now);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<QueuedMail>().Add(message);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
