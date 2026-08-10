namespace Rask.Example.Shared.Features;

// Shared log-list rendering for the three disposal demos promoted out of the former DisposalPage. Each
// demo mounts a probe that appends hook lines to a parent-held list; this renders that list (or an empty
// hint) under a small "Log" heading. Kept in one place so the sync / async / unmount demos don't repeat it.
//
// A component, not a static helper: it returns markup and nothing else, and only a component can reach
// the builder surface (entries are inherited members, so a static class sees none of them).
internal sealed partial class DisposalDemoLog : Component
{
    public required IReadOnlyList<string> Entries { get; set; }

    public required string ListId { get; set; }

    protected override Component? Render() =>
        [
            H3.Class("h6 text-secondary text-uppercase small mt-4")["Log"],
            Entries.Count == 0
                ? P.Class("text-secondary small mb-0")["Empty — mount and unmount the probe."]
                : Ol
                    .Class("list-group list-group-numbered list-group-flush")
                    .Id(ListId)[Entries.Select((line, i) => Li
                        .Key(i)
                        .Class("list-group-item ps-2 small")[Code.Class("small")[line]]).ToArray()]
        ];
}
