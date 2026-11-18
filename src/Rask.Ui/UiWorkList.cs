using Rask.Background;

namespace Rask;

/// <summary>
///     The signed-in user's background work, in the page: each running job with its bar and what it last said, then each one
///     lately done, warned, failed or stopped, a batch as one group — the list <see cref="UiWorkNotices" /> opens, drawn where it
///     is written.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Card[
///         Ui.Heading["Imports"],
///         Ui.WorkList.Lane("imports")
///     ]
///     </code>
///     <para>
///     Not a Flux component: Flux's <see cref="UiProgress" /> and <see cref="UiBadge" /> in rows, an addition the owner
///     approved (2026-11-17, #1293). It shows what the bell shows — this user's work and their tenant's shared work they may run,
///     read again every poll and moved by what the jobs publish — and says in a polite live region when a job it saw running
///     ends. It raises no toast: that is the bell's.
///     </para>
/// </remarks>
public sealed partial class UiWorkList : Component
{
    private readonly UiWorkAnnouncer _announcer = new();

    /// <summary>Only the work of this lane — a pump's, a mailbox's. Unset, all of it.</summary>
    public string? Lane { get; set; }

    // The work to show instead of the user's own: the kit's tests' seam.
    private IReadOnlyList<WorkReported>? Shown { get; set; }

    /// <summary>Shows <paramref name="work" /> instead of the user's own — for the kit's tests.</summary>
    internal UiWorkList Showing(IReadOnlyList<WorkReported> work)
    {
        Shown = work;
        return this;
    }

    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var work = Shown ?? UiWorkFeed.Read(Lane);
        _announcer.Hear(work);
        return Div.Attributes(("data-ui-work-list-of", ""))[UiWorkRow.Rows(work), _announcer.Region()];
    }
}
