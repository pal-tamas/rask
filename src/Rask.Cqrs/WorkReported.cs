using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     How a piece of background work its enqueuer watches is getting on: published by Rask.Jobs when a job enqueued with
///     <c>.Noticed("Import of prices.csv")</c> starts, reports (<c>Jobs.Report(done, of)</c>) and ends.
/// </summary>
/// <remarks>
///     <para>
///         <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> show it, and only to the user who enqueued the job, in the tenant they
///         enqueued it in: who it is for travels with the event and never across the wire. Reports are kept to the latest and
///         published at most about four times a second, so an <see cref="IEventHandler{TEvent}" /> of it — the mail that says
///         an import is done — sees each state change without a flood.
///     </para>
///     <para>
///         Why a job failed is not in it: the bell says <see cref="WorkState.Failed" />, and the reason stays on the
///         operator's board.
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
    /// <summary>The user the work is for: only they see it.</summary>
    internal Guid? Owner { get; init; }

    /// <summary>The tenant the work was enqueued in: only that tenant sees it.</summary>
    internal Guid? Tenant { get; init; }

    /// <summary>The lane the work runs in, for a list of one lane's work.</summary>
    internal string? Lane { get; init; }

    /// <summary>Whether it has ended: done, failed or stopped.</summary>
    internal bool HasEnded => State is not WorkState.Running;
}
