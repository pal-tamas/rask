using System.Reflection.Metadata;
using Microsoft.Extensions.Hosting;
using Rask.Core.HotReload;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.HotReload.Tests;

/// <summary>
///     The dev channel end to end, in-process: an applied update repaints every open live session and is
///     then announced on the wire.
///     <para>
///         Nothing else asserts this. <c>HotReloadMessageTests</c> pins the frame's JSON and its
///         Development-only gating, <c>RerenderAllAsyncTests</c> covers the broadcast plumbing against a
///         detached session, and <c>HotReloadPhaseTests</c> pins the coordinator's phase order — but no
///         test watched the frame arrive on a real socket. The watch E2E does, and it is opt-in and slow;
///         this runs in the default gate.
///     </para>
///     <para>
///         Serialized, because the hot-reload session registry is process-global and
///         <c>RerenderAllForHotReloadAsync</c> awaits every registered session in turn — two of these
///         running concurrently would repaint each other's sessions.
///     </para>
/// </summary>
[Collection("HotReload")]
public sealed class AppliedFrameReachesTheBrowserTests : IDisposable
{
    public AppliedFrameReachesTheBrowserTests() => HotReloadApp.Heading = HotReloadApp.Original;

    public void Dispose() => HotReloadApp.Heading = HotReloadApp.Original;

    /// <summary>
    ///     The guard that keeps the rest of this file honest. Every assertion below is gated on the
    ///     runtime feature switch, so if it were ever off they would all pass while proving nothing —
    ///     which is exactly what happens to the agreement check in <c>HotReloadMessageTests</c> under the
    ///     Release unit gate, where the SDK turns the switch off.
    /// </summary>
    [Fact]
    public void The_feature_switch_is_on_in_this_assembly() =>
        Assert.True(
            MetadataUpdater.IsSupported,
            "MetadataUpdaterSupport must stay true in this csproj — without it every hot-reload gate is "
            + "closed and the tests in this file pass vacuously.");

    [Fact]
    public async Task An_apply_repaints_the_session_and_then_announces_it()
    {
        await using var session = await ConnectedSession.Connect<HotReloadApp>(
            environment: Environments.Development);

        // Assert the subscription actually happened before relying on it, so a later timeout can't be
        // misread as "the frame never arrived" when the cause was a closed gate.
        Assert.True(RaskEndpointExtensions.IsDevHotReloadEnabled(session.Host.Services));

        HotReloadApp.Heading = "after-the-edit";
        RaskHotReloadHandler.UpdateApplication(updatedTypes: null);

        var frames = await ReadUntilHotReloadAsync(session);

        // Order is the whole point of the phase design: the repaint must be on the wire BEFORE the
        // announcement, or the pill claims an update the user cannot see yet.
        var announced = frames.FindIndex(f => f.Contains("\"type\":\"hotReload\"", StringComparison.Ordinal));
        Assert.True(announced >= 0, $"no hotReload frame arrived. Frames: {string.Join(" | ", frames)}");

        var repainted = frames.FindIndex(f => f.Contains("after-the-edit", StringComparison.Ordinal));
        Assert.True(repainted >= 0, $"the session never repainted. Frames: {string.Join(" | ", frames)}");
        Assert.True(repainted < announced, "the applied frame must follow the repaint, not precede it.");

        Assert.Equal(LivePayload.HotReloadAppliedJson, frames[announced]);
        Assert.DoesNotContain(frames, f => f.Contains("\"status\":\"unknown\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_session_whose_socket_closed_does_not_break_the_broadcast()
    {
        await using var dead = await ConnectedSession.Connect<HotReloadApp>(
            environment: Environments.Development);
        await using var alive = await ConnectedSession.Connect<HotReloadApp>(
            environment: Environments.Development);

        await dead.Ws.CloseAsync(
            System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        HotReloadApp.Heading = "after-the-edit";
        RaskHotReloadHandler.UpdateApplication(updatedTypes: null);

        var frames = await ReadUntilHotReloadAsync(alive);

        Assert.Contains(frames, f => f.Contains("\"type\":\"hotReload\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Production_never_announces()
    {
        // The gate asserted on the wire rather than as a boolean: a Production host must subscribe to
        // nothing, so an apply reaches the socket as silence.
        await using var session = await ConnectedSession.Connect<HotReloadApp>();

        Assert.False(RaskEndpointExtensions.IsDevHotReloadEnabled(session.Host.Services));

        HotReloadApp.Heading = "after-the-edit";
        RaskHotReloadHandler.UpdateApplication(updatedTypes: null);

        var frames = await ReadFramesAsync(session, TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(frames, f => f.Contains("\"type\":\"hotReload\"", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Collects frames until the announcement lands. The repaint runs on a background Task, so this
    ///     waits on the frame rather than on a fixed delay.
    /// </summary>
    private static async Task<List<string>> ReadUntilHotReloadAsync(ConnectedSession session)
    {
        var frames = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var frame = await session.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(3));
            if (frame is null)
            {
                continue;
            }

            frames.Add(frame);
            if (frame.Contains("\"type\":\"hotReload\"", StringComparison.Ordinal))
            {
                break;
            }
        }

        return frames;
    }

    private static async Task<List<string>> ReadFramesAsync(ConnectedSession session, TimeSpan window)
    {
        var frames = new List<string>();
        var deadline = DateTime.UtcNow + window;

        while (DateTime.UtcNow < deadline)
        {
            var frame = await session.Ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500));
            if (frame is not null)
            {
                frames.Add(frame);
            }
        }

        return frames;
    }
}

/// <summary>Serializes every hot-reload test in this assembly — the registry it drives is process-global.</summary>
[CollectionDefinition("HotReload", DisableParallelization = true)]
public sealed class HotReloadCollection;
