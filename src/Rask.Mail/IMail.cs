namespace Rask.Mail;

/// <summary>
/// Queues transactional email onto the app's database. Inject it and call <see cref="SendAsync"/> to send an
/// email as soon as the processor next polls, or
/// <see cref="ScheduleAsync(Email, TimeSpan, CancellationToken)"/> to send it later.
/// </summary>
public interface IMail
{
    /// <summary>Queues an email to send as soon as possible.</summary>
    Task SendAsync(Email email, CancellationToken cancellationToken = default);

    /// <summary>Queues an email to send no earlier than <paramref name="delay"/> from now.</summary>
    Task ScheduleAsync(Email email, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>Queues an email to send no earlier than <paramref name="runAt"/>.</summary>
    Task ScheduleAsync(Email email, DateTimeOffset runAt, CancellationToken cancellationToken = default);
}
