using System.Reflection;
using System.Reflection.Metadata;
using Rask.Core.HotReload;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.HotReload;

/// <summary>
///     The hot-reload coordinator: which registries refresh, in what order, and what survives a
///     failure. Shares the ScopedAssets collection because a refresh mutates the shared registry.
/// </summary>
[Collection("ScopedAssets")]
public class HotReloadPhaseTests
{
    private static readonly Assembly[] _thisAssembly = [typeof(HotReloadPhaseTests).Assembly];

    public HotReloadPhaseTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public async Task RunPhases_InvokesEveryRefreshTarget()
    {
        // Proves the reflective loop actually resolves and calls all six generated classes — the
        // thing the old nested-sentinel tests could not reach.
        var called = new List<string>();
        using (RefreshTargets.Arm(
                   () => called.Add("css"), () => called.Add("js"),
                   () => called.Add("cqrs"), () => called.Add("jobs"), () => called.Add("outbox")))
        {
            await RunPhasesAndWaitForAppliedAsync();
        }

        Assert.Equal(["css", "js", "cqrs", "jobs", "outbox"], called);
    }

    [Fact]
    public async Task RunPhases_RefreshesAssetsBeforeRegistries()
    {
        // Assets first is not cosmetic: phase 4's repaint must observe the new bundle hash, so
        // every refresh has to complete before any session re-renders (asserted below).
        var order = new List<string>();
        using (RefreshTargets.Arm(
                   css: () => order.Add("css"),
                   js: () => order.Add("js"),
                   cqrs: () => order.Add("cqrs")))
        {
            await RunPhasesAndWaitForAppliedAsync();
        }

        Assert.True(order.IndexOf("css") < order.IndexOf("cqrs"));
        Assert.True(order.IndexOf("js") < order.IndexOf("cqrs"));
    }

    [Fact]
    public void RefreshTargetTypeNames_CoversEveryGeneratedRegistry()
    {
        // The coordinator reaches other packages by name, so a generator added without an entry
        // here would silently not hot-reload. Each generator's own suite asserts the name it
        // emits appears in this array; this pins the array itself.
        Assert.Equal(
            [
                "__RaskRoutesRegistry",
                "__RaskCqrsRegistry",
                "__RaskValidatorRegistry",
                "Rask.Jobs.Generated.__RaskJobsRegistry",
                "Rask.Outbox.Generated.__RaskOutboxRegistry"
            ],
            RaskHotReload.RefreshTargetTypeNames);
    }

    [Fact]
    public async Task Repaint_ObservesTheRefreshedCssHash()
    {
        // The regression this whole reordering exists for. When the session re-render ran before
        // the CSS refresh, the frame carried the previous bundle hash and the edit only appeared
        // on the next interaction.
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".w { color: red; }");
        var staleHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        string? hashSeenByRender = null;
        var session = new RecordingSession(
            new Widget(), RenderHarness.EmptyServices(),
            onRender: () => hashSeenByRender ??= ScopedAssetRegistry.GetBundleHash(AssetKind.Css));
        session.RegisterForHotReload();

        using (RefreshTargets.Arm(css: () =>
                   ScopedAssetRegistry.RegisterCss(typeof(Widget), ".w { color: green; }")))
        {
            await RunPhasesAndWaitForAppliedAsync();
        }

        Assert.NotNull(hashSeenByRender);
        Assert.NotEqual(staleHash, hashSeenByRender);
        Assert.Equal(ScopedAssetRegistry.GetBundleHash(AssetKind.Css), hashSeenByRender);
        GC.KeepAlive(session);
    }

    [Fact]
    public async Task AThrowingRefreshTarget_DoesNotStopTheRest_NorTheRepaint()
    {
        var reached = new List<string>();
        var session = new RecordingSession(new Widget(), RenderHarness.EmptyServices());
        session.RegisterForHotReload();

        using (RefreshTargets.Arm(
                   css: () => throw new InvalidOperationException("boom"),
                   js: () => reached.Add("js"),
                   cqrs: () => reached.Add("cqrs")))
        {
            await RunPhasesAndWaitForAppliedAsync();
        }

        Assert.Equal(["js", "cqrs"], reached);
        Assert.Equal(1, session.RenderRequests);
        GC.KeepAlive(session);
    }

    /// <summary>
    ///     A session whose repaint faults must not be announced as applied.
    /// </summary>
    /// <remarks>
    ///     <c>Applied</c> drives the browser's green "Hot reload applied" pill, and the repaint is the
    ///     whole of what the developer can see — so announcing it for an edit that never reached the page
    ///     told them the opposite of the truth, with the only evidence on the server's stderr (#603). The
    ///     per-session catch is still there and still right (one faulting tree must not stop the others),
    ///     but it now reports rather than swallows, and the coordinator withholds the announcement.
    /// </remarks>
    [Fact]
    public async Task ASessionThatFailsToRepaint_IsNotAnnouncedAsApplied()
    {
        // The fault is switched off in the finally, and that is load-bearing rather than tidiness:
        // RegisterForHotReload puts the session in a process-global weak list that prunes only on
        // collection, so a session left throwing would fault every LATER test's repaint too — and those
        // tests would then time out waiting for an announcement this fix correctly withholds.
        var fail = true;
        var session = new RecordingSession(
            new Widget(), RenderHarness.EmptyServices(),
            onRender: () => { if (fail) throw new InvalidOperationException("boom"); });
        session.RegisterForHotReload();

        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnApplied() => applied.TrySetResult();

        RaskHotReload.Applied += OnApplied;
        try
        {
            RaskHotReload.RunPhases(_thisAssembly, null);

            // Asserting an absence needs a wait; the alternative is asserting on a race.
            var announced = await Task.WhenAny(applied.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.NotSame(applied.Task, announced);
        }
        finally
        {
            fail = false;
            RaskHotReload.Applied -= OnApplied;
        }

        Assert.Equal(1, session.RenderRequests);
        GC.KeepAlive(session);
    }

    [Fact]
    public async Task AThrowingRefreshTarget_StillClosesTheStagingWindow()
    {
        // The nastiest failure mode: staging left open silently swallows every later
        // RegisterCss for the life of the process.
        using (RefreshTargets.Arm(css: () => throw new InvalidOperationException("boom")))
        {
            await RunPhasesAndWaitForAppliedAsync();
        }

        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".w { color: red; }");
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(Widget), out _));
    }

    [Fact]
    public async Task RunPhases_WithNoGeneratedRegistrations_LeavesAssetsAlone()
    {
        // An app with no scoped CSS has no __RaskScopedCssRegistration to invoke. Opening a
        // staging window anyway would swap an empty map over the live one on End, wiping
        // registrations that a host had made directly.
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".w { color: red; }");

        await RunPhasesAndWaitForAppliedAsync([]); // no assemblies → nothing to refresh

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(Widget), out _));
    }

    [Fact]
    public async Task RunPhases_AnUnchangedCssSet_DoesNotChurnTheBundleHash()
    {
        // Most applies touch no CSS. The bundle hash is an immutable URL the browser has already
        // cached — rewriting it on every keystroke would defeat that.
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".w { color: red; }");
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        using (RefreshTargets.Arm(css: () =>
                   ScopedAssetRegistry.RegisterCss(typeof(Widget), ".w { color: red; }")))
        {
            await RunPhasesAndWaitForAppliedAsync();
        }

        Assert.Equal(hash, ScopedAssetRegistry.GetBundleHash(AssetKind.Css));
    }

    [Fact]
    public async Task RunPhases_DropsACssEntryWhoseFileWasDeleted()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".w { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(OtherWidget), ".o { color: blue; }");

        // The regenerated RefreshAll no longer mentions Widget — its .css file was deleted.
        using (RefreshTargets.Arm(css: () =>
                   ScopedAssetRegistry.RegisterCss(typeof(OtherWidget), ".o { color: blue; }")))
        {
            await RunPhasesAndWaitForAppliedAsync();
        }

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(Widget), out _));
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(OtherWidget), out _));
    }

    [Fact]
    public void TheCoreAssembly_DeclaresExactlyOneMetadataUpdateHandler()
    {
        // Order across multiple handlers is undefined by the runtime, and Rask's phases are
        // order-dependent. This is what stops a future PR from quietly reintroducing that.
        var handlers = typeof(Component).Assembly
            .GetCustomAttributes<MetadataUpdateHandlerAttribute>()
            .ToArray();

        var handler = Assert.Single(handlers);
        Assert.Equal(typeof(RaskHotReloadHandler), handler.HandlerType);
    }

    [Fact]
    public void RefreshTargetTypeNames_HasNoDuplicates()
    {
        Assert.Equal(
            RaskHotReload.RefreshTargetTypeNames.Length,
            RaskHotReload.RefreshTargetTypeNames.Distinct(StringComparer.Ordinal).Count());
    }

    private static async Task RunPhasesAndWaitForAppliedAsync(IReadOnlyList<Assembly>? assemblies = null)
    {
        // The repaint phase is dispatched off the hot-reload agent's thread; wait on the
        // coordinator's own completion signal rather than sleeping.
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnApplied() => applied.TrySetResult();

        RaskHotReload.Applied += OnApplied;
        try
        {
            RaskHotReload.RunPhases(assemblies ?? _thisAssembly, null);
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            RaskHotReload.Applied -= OnApplied;
        }
    }

    private sealed class Widget : Component
    {
        protected override Component? Render() => Div;
    }

    private sealed class OtherWidget : Component
    {
        protected override Component? Render() => Div;
    }

    private sealed class RecordingSession : LiveSessionBase
    {
        private readonly Action? _onRender;

        public RecordingSession(Component view, IServiceProvider services, Action? onRender = null)
            : base(view, services, LiveDiffMode.Auto)
            => _onRender = onRender;

        public int RenderRequests;

        protected override Task RequestRenderInternalAsync(bool publishOnly)
        {
            RenderRequests++;
            _onRender?.Invoke();
            return Task.CompletedTask;
        }

        protected override Task RenderInScopeCoreAsync() => Task.CompletedTask;

        protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => default;
    }
}
