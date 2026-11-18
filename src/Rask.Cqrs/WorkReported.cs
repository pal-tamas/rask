using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     How a piece of background work its enqueuer watches is getting on: published by Rask.Jobs when a job enqueued with
///     <c>.Noticed("Import of prices.csv")</c> starts, reports (<c>Jobs.Report(done, of)</c>) and ends.
/// </summary>
/// <remarks>
///     <para>
///         <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> show it to the user who enqueued the job, in the tenant they enqueued it
///         in — and, for a job enqueued <c>.ByTenant()</c>, to that tenant's other users who pass its handler's
///         <c>[Authorize]</c>: who it is for travels with the event and never across the wire. Reports are kept to the latest and
///         published at most about four times a second, so an <see cref="IEventHandler{TEvent}" /> of it — the mail that says
///         an import is done — sees each state change without a flood.
///     </para>
///     <para>
///         Why a job failed is not in it: the bell says <see cref="WorkState.Failed" />, and the error stays on the
///         operator's board. <see cref="Detail" /> is only what the handler chose to say (<c>Jobs.Warn</c>, <c>Jobs.Refuse</c>,
///         <c>Jobs.Note</c>).
///     </para>
/// </remarks>
/// <param name="Id">The job's row id: what <c>Jobs.Stop(id)</c> takes.</param>
/// <param name="Title">What the work is, in words for the person who started it — the title of <c>.Noticed(…)</c>.</param>
/// <param name="Progress">What it last said of itself, if anything: <c>Jobs.Report("1,200 of 5,000 rows")</c>.</param>
/// <param name="Done">How much of <paramref name="Of" /> it has done, when it reported a count.</param>
/// <param name="Of">How much there is to do, when it reported a count; null draws a bar that moves without an end.</param>
/// <param name="State">Where it stands.</param>
/// <param name="At">When this was so (UTC).</param>
public sealed record WorkReported(long Id, string Title, string? Progress, int? Done, int? Of, WorkState State, DateTime At) : IEvent
{
    /// <summary>
    ///     The batch it was enqueued in — every job of one <c>Jobs.Enqueue(jobs)</c> carries the same — or null for a job
    ///     enqueued on its own. The kit draws a batch as one group.
    /// </summary>
    public Guid? Batch { get; init; }

    /// <summary>
    ///     What its handler said of how it ended, for the person who started it — <c>Jobs.Warn("No new rows")</c>,
    ///     <c>Jobs.Refuse(…)</c>, <c>Jobs.Note(…)</c> — or null. Never the error a failure threw.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>The user the work is for: they see it, and — when it is <see cref="Shared" /> — their tenant too.</summary>
    internal Guid? Owner { get; init; }

    /// <summary>The tenant the work was enqueued in: only that tenant sees it.</summary>
    internal Guid? Tenant { get; init; }

    /// <summary>The lane the work runs in, for a list of one lane's work.</summary>
    internal string? Lane { get; init; }

    /// <summary>Whether it was enqueued <c>.ByTenant()</c>: its tenant's users who may run its kind see it too.</summary>
    internal bool Shared { get; init; }

    /// <summary>The job's registered type name: whose <c>[Authorize]</c> a colleague must pass to see shared work.</summary>
    internal string? Kind { get; init; }

    /// <summary>
    ///     Whether the one it is shown to started it, when that is known: the board says so per reader, and work that is not
    ///     <see cref="Shared" /> reaches nobody but its starter. Null for shared work heard live without its owner.
    /// </summary>
    internal bool? Mine { get; init; }

    /// <summary>Whether <paramref name="viewer" /> started it: <see cref="Mine" /> when known, else its owner when it travels with it.</summary>
    internal bool IsMineTo(Guid? viewer) => Mine ?? (Owner is { } owner ? owner == viewer : !Shared);

    /// <summary>Whether it has ended: done, warned, failed or stopped.</summary>
    internal bool HasEnded => State is not WorkState.Running;
}
