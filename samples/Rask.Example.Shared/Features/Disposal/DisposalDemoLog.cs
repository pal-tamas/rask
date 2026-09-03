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

    // Entries is a list this component does not own: the demo above it APPENDS to the same List<string>
    // and re-renders itself. The reference never changes, so the props check (EqualityComparer<T>.Default,
    // i.e. reference equality here) reports no change and the render cache replays the stale subtree —
    // the log stays on "Empty — mount and unmount the probe." forever. Same invariant as
    // ExternalStateInvalidationTests: a component deriving UI from state it does not own must either
    // subscribe to a change source or opt out of the cache, and a bare List has no event to subscribe to.
    //
    // Worth being explicit about why this only started mattering: the generated factory used to re-apply
    // every property on every render, so nothing was ever actually render-cached and reading a mutated
    // collection happened to work. The chain surface writes only what the call site names, which is what
    // makes the cache real — and this the first place it bit.
    protected override bool BypassRenderCache => true;

    protected override Component? Render() =>
        [
            H3.Class("text-base font-semibold text-ui-muted uppercase text-sm mt-4")["Log"],
            Entries.Count == 0
                ? P.Class("text-ui-muted text-sm mb-0")["Empty — mount and unmount the probe."]
                : Ol
                    .Class($"{Tw.ListGroup} list-decimal list-inside divide-y divide-ui-line")
                    .Id(ListId)[Entries.Select((line, i) => Li
                        .Key(i)
                        .Class($"{Tw.ListGroupItem} ps-2 text-sm")[Code.Class("text-sm")[line]]).ToArray()]
        ];
}
