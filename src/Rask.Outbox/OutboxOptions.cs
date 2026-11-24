namespace Rask.Outbox;

/// <summary>Options for the <see cref="OutboxProcessor{TContext}"/>.</summary>
public sealed class OutboxOptions
{
    /// <summary>How often the processor polls the outbox table as a safety net; a save that wrote rows wakes it at once. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages to claim and publish per poll. Default 100, maximum 1000.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long a claimed message stays invisible to other processor instances. Default 5 minutes.
    /// </summary>
    /// <remarks>
    /// This is the recovery window, not a timeout: nothing cancels a dispatch that overruns it. A processor
    /// that dies mid-dispatch makes its work claimable again after this long, so it must comfortably exceed
    /// the slowest handler you run — set it too low and a slow handler's message is picked up by a second
    /// instance while the first is still running it, which is the duplicate the lease exists to prevent.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many times to retry a failing message before it is left for inspection. Default 10.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// How long published messages are kept before being purged. <see cref="TimeSpan.Zero"/> (or any
    /// non-positive value) keeps them forever. Default 7 days, matching <c>Rask.Jobs</c> and <c>Rask.Mail</c>.
    /// <para>
    /// Only messages that were successfully published are ever removed. A dead letter has no
    /// <see cref="OutboxMessage.ProcessedAt"/>, so retention never deletes the one row you still need to
    /// look at, whatever value you set here.
    /// </para>
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether a durable handler runs inside one <c>Db.Transaction</c> with the mark that says its row is done, so what
    /// it writes to the database and the mark commit together — or neither does. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, the handler's writes commit on their own and the mark follows: a crash between the two runs the handler
    /// again, so its writes must be safe to make twice. On, that window is gone — a handler that throws, a mark that
    /// fails, a lease lost to another instance: each rolls the handler's writes back with the mark, and the next run
    /// starts from where the last one was — exactly once, for what goes to the database. A call outside it (a mail, a
    /// partner's API) is still at-least-once; give it <c>DurableEvent.Current.MessageId</c> as its idempotency key.
    /// </para>
    /// <para>
    /// The transaction is the handler's own, one per row — an aggregate's <c>static Task On(E e)</c> writes its aggregate
    /// in it, never in the transaction that raised the event. It stays open while the handler runs, so keep a slow call
    /// outside the database (or hand it to a job) rather than holding the write open across it.
    /// </para>
    /// </remarks>
    public bool HandlerInTransaction { get; set; }

    /// <summary>The ceiling on <see cref="BatchSize"/> — see <see cref="OutboxOptionsValidator"/> for why there is one.</summary>
    internal const int MaxBatchSize = 1000;

    /// <summary>
    /// How long a message that is already being published may keep publishing after the host is asked to
    /// stop.
    /// <para>
    /// On <c>SIGTERM</c> the processor immediately stops picking up <em>new</em> messages, but the one
    /// already in your handler is given this long to finish rather than being cancelled mid-call.
    /// </para>
    /// <para>
    /// A message that outlives the grace is cancelled, has the attempt its claim counted rolled back, and
    /// is re-published from the top on the next boot. The roll-back is deliberate: a redeploy is not a
    /// failure, and letting it count would march never-failing work toward its dead letter at the cadence
    /// you deploy. Handlers must be idempotent either way — an interrupted message always re-runs whole.
    /// </para>
    /// <para>
    /// Cannot exceed <c>HostOptions.ShutdownTimeout</c>: once that elapses the host stops waiting for
    /// hosted services, so a grace longer than it silently does not happen. Default 5s;
    /// <see cref="TimeSpan.Zero"/> cancels immediately.
    /// </para>
    /// </summary>
    public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
