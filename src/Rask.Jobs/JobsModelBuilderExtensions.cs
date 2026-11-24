using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Background;

/// <summary>Model-building helper for the jobs tables.</summary>
public static class JobsModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="Job"/> and <see cref="RecurringJobState"/> tables. Call from your context's
    /// <c>OnModelCreating</c>, then create the schema with <c>rask db add AddJobs &amp;&amp; rask db update</c>.
    /// </summary>
    public static ModelBuilder AddRaskJobs(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new JobConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringJobStateConfiguration());
        return modelBuilder;
    }

    /// <summary>
    ///     Runs the aggregate's <c>static Task On(TEvent e)</c> as a job: the outbox's row for it only enqueues the job, and
    ///     the job runs the handler — with a job's lane, lease, progress and dead letter. Shape it with the steps:
    ///     <c>b.Job&lt;FileQueued&gt;().InTenantLane("loads").UnlessWaiting(e =&gt; e.File.ToString())</c>.
    /// </summary>
    /// <remarks>
    ///     For a handler that runs long — an import, a pull from a partner — or that one tenant must run one at a time.
    ///     A job's lease is renewed while it runs, so a long one is never picked up twice, and one its host died with on
    ///     its last attempt is swept to a dead letter. Without Rask.Jobs' processor in the app, nothing runs the job.
    /// </remarks>
    /// <typeparam name="TEvent">The event the aggregate's <c>On</c> takes.</typeparam>
    /// <param name="builder">The aggregate's builder, in its <c>Configure</c>.</param>
    /// <returns>The steps that place the job: its lane, and the key that makes a second one wait for nothing.</returns>
    public static EventJob<TEvent> Job<TEvent>(this EntityTypeBuilder builder)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(builder);
        return new EventJob<TEvent>(EventJobRoute.Declare(builder.Metadata.ClrType, typeof(TEvent)));
    }
}
