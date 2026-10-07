using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's Web Locks API from Rask.Web — coordinate work across the tabs and workers of one origin. This demo holds
///     an exclusive lock for two seconds: open this page in a second tab and click "Hold" in both — the second waits
///     for the first to release. "Try (no wait)" asks with <c>ifAvailable</c>, so while the lock is held it is handed
///     no lock at once instead of waiting. "Query" snapshots the locks the origin holds and waits for now.
/// </summary>
public sealed partial class WebLocksDemo : Component
{
    private const string LockName = "rask-web-locks-demo";
    private string _status = "(idle)";
    private (Types.LockInfo Lock, string State)[] _snapshot = [];

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Id("locks-hold").OnClick(Hold)["Hold exclusive for 2s"],
                    Ui.Button
                        .Id("locks-try")
                        .OnClick(TryHold)["Try (no wait)"],
                    Ui.Button
                        .Id("locks-query")
                        .OnClick(Query)["Query held locks"]
                ],
                Div.Class("text-sm text-ui-muted mb-1")["Status: ", Code.Id("locks-status")[_status]],
                _snapshot.Length == 0
                    ? Div.Class("text-sm text-ui-muted italic").Id("locks-snapshot")["(query to see held locks)"]
                    : Ul.Class("text-sm mb-0").Id("locks-snapshot")[
                        _snapshot.Select((l, i) => Li.Key($"{i}:{l.Lock.Name}")[$"{l.Lock.Name} — {l.Lock.Mode} — {l.State}"])
                    ]
            ];

    private async Task Hold()
    {
        if (!await Navigator.Locks.IsSupported)
        {
            _status = "not supported";
            return;
        }

        try
        {
            // The lock is held until this handler returns, so other tabs wait for the two seconds.
            await Navigator.Locks.Request(LockName, async _ =>
            {
                _status = "holding — other tabs wait here";
                StateHasChanged(); // shown while the handler still runs, not once it has returned
                await Task.Delay(2000);
            });
            _status = "released";
        }
        catch (JSException ex)
        {
            _status = "failed: " + ex.Message;
        }
    }

    private async Task TryHold()
    {
        try
        {
            var got = false;
            await Navigator.Locks.Request(LockName, new() { IfAvailable = true }, granted => got = granted is not null);
            _status = got ? "try: acquired (and released)" : "try: already held — stood down";
        }
        catch (JSException ex)
        {
            _status = "failed: " + ex.Message;
        }
    }

    private async Task Query()
    {
        try
        {
            var locks = await Navigator.Locks.Query();
            _snapshot = [.. (locks.Held ?? []).Select(l => (l, "held")), .. (locks.Pending ?? []).Select(l => (l, "pending"))];
            _status = $"queried: {locks.Held?.Length ?? 0} held, {locks.Pending?.Length ?? 0} waiting";
        }
        catch (JSException ex)
        {
            _status = "failed: " + ex.Message;
        }
    }
}
