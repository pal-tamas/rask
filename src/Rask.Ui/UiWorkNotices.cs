using System.Globalization;
using Rask.Background;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     The signed-in user's background work, at the header's end: a bell whose badge counts what runs and what ended since they
///     last looked, and a flyout of it — each running job with its bar, each one lately done, warned, failed or stopped with a
///     badge and what its handler said of it, a batch as one group — and, under it, the app's own <see cref="Actions" />.
/// </summary>
/// <remarks>
///     <code>
///     Ui.AppFrame.Brand("Shop").Notices(Ui.WorkNotices)[ … ]
///
///     await Jobs.Enqueue(new ImportPrices(file.Id)).Noticed($"Import of {file.Name}");   // the job it shows
///     Jobs.Report(kept, total);                                                           // its bar, from the handler
///
///     Ui.WorkNotices.Actions(Ui.Link.Href(Routes.LoadsPage().Url())["All loads"])         // a footer under the list
///     </code>
///     <para>
///     Not a Flux component — Flux has no notifications — but drawn of Flux's own parts: <see cref="UiDropdown" /> opening a
///     <see cref="UiPopover" />, a ghost <see cref="UiButton" /> with Flux's <c>bell</c>, a <see cref="UiBadge" /> for the
///     count and <see cref="UiProgress" /> for each bar. An addition the owner approved (2026-11-17, #1293).
///     </para>
///     <para>
///     It shows the jobs this user enqueued <c>.Noticed(…)</c>, in the tenant they enqueued them in, and the ones a colleague
///     enqueued <c>.ByTenant()</c> that this user may run themselves (its handler's <c>[Authorize]</c>): the rows are read
///     again every poll of the job processor, and what the jobs publish moves the bars in between. Opening the flyout clears
///     the count of what ended; what runs keeps counting. When a job it saw running ends, a polite live region says so; when
///     the last job of a batch this user started ends, one toast says how the batch went. <see cref="Lane" /> narrows it to
///     one lane's work.
///     </para>
/// </remarks>
public sealed partial class UiWorkNotices : Component
{
    private readonly UiWorkAnnouncer _announcer = new() { Toasts = true };
    private DateTime? _seen;

    /// <summary>Only the work of this lane — a pump's, a mailbox's. Unset, all of it.</summary>
    public string? Lane { get; set; }

    /// <summary>
    ///     What stands under the list in the flyout — a link to the page of all the work, a button that clears it:
    ///     <c>.Actions(Ui.Link.Href(Routes.LoadsPage().Url())["All loads"])</c>. Unset, nothing does.
    /// </summary>
    public Component? Actions { get; set; }

    // The work to show instead of the user's own: the kit's tests' seam.
    private IReadOnlyList<WorkReported>? Shown { get; set; }

    /// <summary>Shows <paramref name="work" /> instead of the user's own — for the kit's tests.</summary>
    internal UiWorkNotices Showing(IReadOnlyList<WorkReported> work)
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
        _seen ??= UiWorkFeed.Clock.GetUtcNow().UtcDateTime;
        _announcer.Hear(work);
        var news = work.Count(w => !w.HasEnded || w.At > _seen);

        return
        [
            Ui.Dropdown.Position(Ui.DropdownPosition.Bottom).Align(Ui.DropdownAlign.End).OnToggle(Looked)[
                Ui.Button.Variant(Ui.ButtonVariant.Ghost).Size(Ui.ButtonSize.Sm).Square(true).Icon(Ui.IconName.Bell)
                    .Class("relative").Aria("label", Name(news)).Attributes(("data-ui-work-bell", ""))[Badge(news)],
                Ui.Popover.Class("w-80 max-w-[calc(100vw-2rem)]")[
                    Div.Attributes(("data-ui-work-flyout", ""))[
                        Ui.Heading.Class("px-1 pb-1")[RaskStrings.Get(RaskString.WorkNotices, "Background work")],
                        UiWorkRow.Rows(work, "max-h-96 overflow-y-auto px-1"),
                        Actions is null
                            ? null
                            : Div.Class("flex flex-wrap items-center justify-end gap-2 border-t border-zinc-800/10 px-1 pt-2 dark:border-white/20")
                                .Attributes(("data-ui-work-actions", ""))[Actions]
                    ]
                ]
            ],
            _announcer.Region()
        ];
    }

    // Opening it is looking: what ended so far is no longer news. Closing it, too, so what ended while it was open is not.
    private void Looked(bool open) => _seen = UiWorkFeed.Clock.GetUtcNow().UtcDateTime;

    private static string Name(int news) =>
        news == 0
            ? RaskStrings.Get(RaskString.WorkNotices, "Background work")
            : RaskStrings.Get(RaskString.WorkNoticesUnread, "Background work, {0} new", news.ToString(CultureInfo.CurrentCulture));

    // On the bell's corner, read with the bell's own name: the badge itself is not said twice.
    private static Component? Badge(int news)
    {
        if (news == 0)
        {
            return null;
        }

        var count = news > 99 ? "99+" : news.ToString(CultureInfo.CurrentCulture);
        return Ui.Badge.Size(Ui.BadgeSize.Sm).Variant(Ui.BadgeVariant.Solid).Color(Ui.Color.Red).Rounded(true)
                .Class("pointer-events-none absolute -end-1 -top-1 min-w-5 justify-center px-1 tabular-nums")
                .Aria("hidden", "true").Attributes(("data-ui-work-count", ""))[count];
    }
}
