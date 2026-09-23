using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Rask.Mail;

/// <summary>
/// The default <see cref="IMail"/>: renders the email (already done by the builder) and writes one
/// <see cref="QueuedMail"/> row through the app's <see cref="IDbContextFactory{TContext}"/>. The sender is
/// resolved once, at enqueue time — from the message's own <c>From</c> if set, otherwise
/// <see cref="MailOptions.From"/> — so the stored row is self-contained.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the mail table.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MailQueue<TContext>(IDbContextFactory<TContext> contextFactory, MailOptions options, TimeProvider timeProvider) : IMail
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task Add(Email email, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        var runAt = (at ?? timeProvider.GetUtcNow() + (after ?? TimeSpan.Zero)).UtcDateTime;
        var from = email.FromAddress ?? new EmailAddress(options.From, options.FromName);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var message = MailSerializer.ToQueuedMail(email, from, runAt, now);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<QueuedMail>().Add(message);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
