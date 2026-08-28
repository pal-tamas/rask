using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IWebLocks" /> — coordinate work across the tabs/workers of one origin. This demo holds an
///     exclusive lock for two seconds: open this page in a second tab and click "Hold" in both — the second
///     waits for the first to release. "Try (no wait)" uses <c>ifAvailable</c>, so it reports
///     <c>false</c> immediately while the lock is held. "Query" snapshots the locks the origin holds now.
/// </summary>
public sealed partial class WebLocksDemo(IWebLocks locks) : Component
{
    private const string LockName = "rask-web-locks-demo";
    private string _status = "(idle)";
    private bool _holding;
    private IReadOnlyList<LockInfo> _snapshot = [];

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Div.Class($"flex gap-2 flex-wrap items-center {"mb-2"}")[
                    Button.Type("button").Class(Ui.BtnPrimary).Id("locks-hold").OnClickAsync(Hold)[
                        "Hold exclusive for 2s"],
                    Button.Type("button").Class(Ui.BtnOutlinePrimary)
                        .Id("locks-try")
                        .OnClickAsync(TryHold)[
                        "Try (no wait)"],
                    Button.Type("button").Class(Ui.BtnOutlineSecondary)
                        .Id("locks-query")
                        .OnClickAsync(Query)[
                        "Query held locks"]
                ],
                Div.Class("small text-secondary mb-1")["Status: ", Code.Id("locks-status")[_status]],
                _snapshot.Count == 0
                    ? Div.Class("small text-secondary fst-italic").Id("locks-snapshot")["(query to see held locks)"]
                    : Ul.Class("small mb-0").Id("locks-snapshot")[
                        _snapshot.Select(l => Li.Key($"{l.Name}:{l.ClientId}:{l.Held}")[
                            $"{l.Name} — {l.Mode} — {(l.Held ? "held" : "pending")}"])
                    ]
            ]
        ];

    private async Task Hold()
    {
        if (!await locks.IsSupportedAsync())
        {
            _status = "not supported";
            return;
        }

        _holding = true;
        _status = "waiting for the lock…";
        StateHasChanged();
        try
        {
            await locks.RequestAsync(LockName, async () =>
            {
                _status = "holding — other tabs wait here";
                StateHasChanged();
                await Task.Delay(2000);
            });
            _status = "released";
        }
        catch (Exception ex)
        {
            _status = "failed: " + ex.Message;
        }
        finally
        {
            _holding = false;
        }
    }

    private async Task TryHold()
    {
        try
        {
            var got = await locks.TryRequestAsync(LockName, () => Task.CompletedTask);
            _status = got ? "try: acquired (and released)" : "try: already held — stood down";
        }
        catch (Exception ex)
        {
            _status = "failed: " + ex.Message;
        }
    }

    private async Task Query()
    {
        try
        {
            _snapshot = await locks.QueryAsync();
            _status = _holding ? "holding — see snapshot" : $"queried: {_snapshot.Count} lock(s)";
        }
        catch (Exception ex)
        {
            _status = "failed: " + ex.Message;
        }
    }
}
