namespace Rask.Example.Shared.Features;

// Shared log-list rendering for the three disposal demos promoted out of the former DisposalPage. Each
// demo mounts a probe that appends hook lines to a parent-held list; this renders that list (or an empty
// hint) under a small "Log" heading. Kept in one place so the sync / async / unmount demos don't repeat it.
internal static class DisposalDemoLog
{
    public static Component Render(IReadOnlyList<string> entries, string id) =>
        [
            H3(Class: "h6 text-secondary text-uppercase small mt-4")["Log"],
            entries.Count == 0
                ? P(Class: "text-secondary small mb-0")["Empty — mount and unmount the probe."]
                : Ol(Class: "list-group list-group-numbered list-group-flush",
                    Id: id)[entries.Select((line, i) => Li(Key: i,
                    Class: "list-group-item ps-2 small")[Code(Class: "small")[line]]).ToArray()]
        ];
}
