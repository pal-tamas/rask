using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     Test app for the dispose/render teardown race. Clicking the button arms a one-shot gate;
///     the resulting re-render enters <see cref="Render" />, signals <see cref="RenderEntered" />
///     (so a test knows a render is now holding the session's <c>_renderLock</c> and is mid-walk),
///     then blocks on <see cref="ReleaseRender" /> until the test releases it. That lets a test
///     hold a render in flight deterministically and dispose the session underneath it.
/// </summary>
public sealed partial class GatedRenderApp : Component
{
    /// <summary>Completes when a gated render has entered <see cref="Render" />.</summary>
    public static TaskCompletionSource RenderEntered { get; private set; } = NewLatch();

    /// <summary>Released by the test to let the blocked render finish.</summary>
    public static ManualResetEventSlim ReleaseRender { get; } = new(false);

    private bool _gateNextRender;

    /// <summary>Resets the static gate between tests (the statics are shared per app type).</summary>
    public static void Reset()
    {
        RenderEntered = NewLatch();
        ReleaseRender.Reset();
    }

    protected override Component? Render()
    {
        if (_gateNextRender)
        {
            // One-shot: only the click-triggered render gates, never the GET/hello renders.
            _gateNextRender = false;
            RenderEntered.TrySetResult();
            ReleaseRender.Wait();
        }

        return
        [
            Doctype(),
            new Html()[
                new Head()[new Title()["gated"]],
                new Body()[
                    new P()["gated-render"],
                    Button(OnClickAsync: GateAsync)["go"]
                ]
            ]
        ];
    }

    private Task GateAsync()
    {
        _gateNextRender = true;
        return Task.CompletedTask;
    }

    private static TaskCompletionSource NewLatch() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
