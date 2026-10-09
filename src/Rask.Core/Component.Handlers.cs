using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    internal string RegisterHandler(Delegate handler) =>
        RegisterHandler(handler, this);

    internal string RegisterHandler(Delegate handler, Component owner)
    {
        // Two roles, kept deliberately separate:
        //
        //  * slotComponent — CurrentParent, i.e. the component whose SUBTREE this element is being
        //    serialized in. It anchors the numbering: each component numbers the handlers appearing in
        //    its own subtree 0, 1, 2… in walk order, so nothing outside that subtree can renumber them.
        //    Note "serialized in", not "rendered by": an element built in a parent's Render() and passed
        //    into a wrapper as indexer children serializes inside the WRAPPER's scope and takes a slot
        //    there. So `Card()[cond ? Button(a) : null, Button(b)]` still renumbers Button(b) when the
        //    condition flips — the renumbering is bounded to that one wrapper rather than running to the
        //    end of the page, which is the guarantee, and it is what CleanSubtreeCache relies on.
        //  * dispatchOwner — the component to dirty-mark after the handler runs. For lambdas / method
        //    groups that close over `this` inside a Component subclass (a lambda bumping a field,
        //    a method group handed to OnAnySubmit), DelegateOwner resolves the component that owns the state, so
        //    an element built in ComponentA.Render() but rendered inside ComponentB's subtree (passed
        //    as a child of a composite wrapper) still re-renders A. It also unwraps a closure that
        //    captured `this` alongside a local (`() => _active = index`).
        //
        // Anchoring the SLOT to that resolved target instead would undo the whole point: a callback
        // passed down into a wrapper would consume a slot on the component it was passed FROM, and so
        // shift that component's own ids from outside its own render.
        var slotComponent = owner;
        var dispatchOwner = DelegateOwner.Resolve(handler) is { } target ? target : owner;

        // `this` is always the render root — LiveRenderContext.RegisterHandler dispatches to _root — so
        // the id source, the generation and the handler map all live on one node.
        var rootState = Live.HandlerState ??= new HandlerState();
        var map = Live.Handlers ??= new Dictionary<string, (Component, Delegate)>(StringComparer.Ordinal);

        var slotState = slotComponent.Live.HandlerState ??= new HandlerState();

        // Slot ids are drawn from ONE root's sequence, so a component that arrives under a different
        // root has to re-mint: handing back an id the new root never issued would let the number it
        // does issue next collide, wiring two elements to a single entry in the map. Reachable through
        // ordinary API — rendering the same instance a second time builds a fresh root.
        if (!ReferenceEquals(slotState.MintedUnder, rootState))
        {
            slotState.MintedUnder = rootState;
            slotState.Slot0Id = null;
            slotState.RestIds = null;
            slotState.Stamp = 0;
        }

        // Per-component slot index, reset the first time this component registers in a given root
        // render. The generation stamp (rather than a "reset me" call at scope entry) is what keeps a
        // component that somehow gets walked twice in one frame numbering forward instead of reusing
        // its own slots.
        if (slotState.Stamp != rootState.Generation)
        {
            slotState.Stamp = rootState.Generation;
            slotState.LocalCount = 0;
        }

        var id = SlotId(slotState, rootState, slotState.LocalCount++);
        map[id] = (dispatchOwner, handler);
        return id;
    }

    /// <summary>
    ///     Open a new handler-slot generation on this render root, telling every component to restart
    ///     its own slot numbering the next time it registers. Called once per live render pass, from
    ///     <see cref="LiveRenderContext" />'s constructor.
    /// </summary>
    internal void BeginHandlerGeneration() =>
        (Live.HandlerState ??= new HandlerState()).Generation =
            Interlocked.Increment(ref _renderGenerationSource);

    // The id for one of a component's slots: minted the first time a render reaches that slot, then
    // held for the component's lifetime. Existing slots never renumber, which is the invariant the
    // clean-subtree cache rests on — an unchanged component's baked-in ids are still exactly the ids a
    // walk would issue now, no matter what the rest of the page did.
    //
    // A number is never handed to a SECOND COMPONENT. When a component unmounts, its ids simply stop
    // being re-registered, so a stale in-flight event for a removed element resolves to nothing and
    // safely no-ops; a free-list would instead redirect that click to whichever component took the
    // number. The number space therefore grows with CUMULATIVE rather than concurrent slots — but the
    // id STRING is cached per slot, so a steady-state re-render allocates nothing however large the
    // numbers get, and only a brand-new slot past the interned table costs a one-off string.
    //
    // WITHIN one component a slot is still reused: a component that renders [Cancel, Delete] while
    // editing and [Delete] otherwise gives Delete slot 0 — Cancel's — once editing ends, so an
    // in-flight click on Cancel can land on Delete. That is unchanged from the page-wide counter this
    // replaced (which reassigned far more aggressively), and HandlerFrameShape.Accepts is what narrows
    // it: a frame can only run a handler it can actually feed. Closing it completely would mean keying
    // slots on something stabler than emission order, which is a separate change.
    //
    // Slot 0 is a scalar, so the common single-handler component never allocates an array; slots 1..
    // share one geometrically grown array, allocated only when a second handler appears.
    private static string SlotId(HandlerState slotState, HandlerState rootState, int slot)
    {
        if (slot == 0)
        {
            return slotState.Slot0Id ??= HandlerId(rootState.NextNumber++);
        }

        var index = slot - 1;
        var rest = slotState.RestIds;
        if (rest is null || index >= rest.Length)
        {
            // Double (or jump straight to the needed size) so a component registering many handlers
            // pays O(n) total array allocation, not O(n²). New entries default to null = unassigned.
            var grown = new string[Math.Max(index + 1, Math.Max(2, (rest?.Length ?? 0) * 2))];
            if (rest is { Length: > 0 })
            {
                Array.Copy(rest, grown, rest.Length);
            }

            slotState.RestIds = rest = grown;
        }

        return rest[index] ??= HandlerId(rootState.NextNumber++);
    }

    // The id already issued for a slot, or null when that slot has never been reached. Capture and
    // replay read through this so neither can mint a number as a side effect.
    private static string? IssuedSlotId(HandlerState state, int slot) => slot switch
    {
        0 => state.Slot0Id,
        _ when state.RestIds is { } rest && slot - 1 < rest.Length => rest[slot - 1],
        _ => null,
    };

    private static string[] BuildSmallHandlerIds(int n)
    {
        var arr = new string[n];
        for (var i = 0; i < n; i++)
        {
            arr[i] = "h" + i;
        }

        return arr;
    }

    // Overflow path for renders with > _smallHandlerIds.Length handlers in one root.
    // The prebake covers 1024 handlers per render — orders of magnitude past anything
    // realistic. When a VirtualizeModel / huge keyed list pushes past that, stackalloc + a
    // direct TryFormat skips the int.ToString allocation that `"h" + n` would force.
    private static string CreateLargeHandlerId(int n)
    {
        Span<char> buf = stackalloc char[12];
        buf[0] = 'h';
        return n.TryFormat(buf[1..], out var written, provider: CultureInfo.InvariantCulture)
            ? new string(buf[..(1 + written)])
            : "h" + n.ToString(CultureInfo.InvariantCulture);
    }

    // The id for slot number n. Shared by issue / capture / replay so all three agree by construction.
    // Unsigned compare so a wrapped (negative) number falls to the format path and yields "h-1" rather
    // than indexing the table out of bounds. Numbers now accumulate over a session's whole life instead
    // of resetting each render, so overflow is reachable in principle — 2^31 slots — where it was not.
    private static string HandlerId(int n) =>
        (uint)n < (uint)_smallHandlerIds.Length ? _smallHandlerIds[n] : CreateLargeHandlerId(n);

    internal ValueTask<bool> TryInvokeHandlerAsync(string id, JsonElement payload)
        => TryInvokeHandlerAsync(id, payload, null, CancellationToken.None);

    // The dispatch entry. Forwarding rather than async on purpose: with no devtools probe attached, the path every
    // event takes gains a static read and a branch — no state-machine field, no timestamp — over calling the core
    // directly. Anything the devtools need lives in ObserveHandlerAsync, which only an attached probe reaches.
    internal ValueTask<bool> TryInvokeHandlerAsync(
        string id, JsonElement payload, IServiceProvider? services, CancellationToken dispatchToken = default)
        => RaskDevToolsHook.Active is { } devTools
            ? ObserveHandlerAsync(devTools, id, payload, services, dispatchToken)
            : TryInvokeHandlerCoreAsync(id, payload, services, dispatchToken);

    // The devtools' view of one dispatch: which component owned the handler, how long it ran, and whether it threw.
    //
    // A fault an ErrorBoundary catches is answered with `true` by the core rather than thrown, so it reaches
    // HandlerEnded without one; the boundary trip itself is what reports it.
    private async ValueTask<bool> ObserveHandlerAsync(
        IRaskDevToolsProbe devTools, string id, JsonElement payload, IServiceProvider? services,
        CancellationToken dispatchToken)
    {
        // Resolved before the invoke: a render the handler triggers rebuilds the handler map.
        if (Live.Handlers is null || !Live.Handlers.TryGetValue(id, out var entry))
        {
            return await TryInvokeHandlerCoreAsync(id, payload, services, dispatchToken).ConfigureAwait(false);
        }

        var (owner, _) = entry;
        var start = devTools.HandlerStarting(owner, id, payload);
        try
        {
            var handled = await TryInvokeHandlerCoreAsync(id, payload, services, dispatchToken).ConfigureAwait(false);
            devTools.HandlerEnded(owner, id, start, fault: null);
            return handled;
        }
        catch (Exception ex)
        {
            devTools.HandlerEnded(owner, id, start, ex);
            throw;
        }
    }

    // No explicit debugger break in the catch below, on purpose. A handler's exception leaves the user's code
    // for Rask's, which a debugger with Just My Code already reports as user-unhandled: it stops on the throw
    // line itself. An explicit Debugger.BreakForUserUnhandledException here made it stop a SECOND time at the
    // same line — verified under VS Code's F5 — so the call only added a Continue press.
    private async ValueTask<bool> TryInvokeHandlerCoreAsync(
        string id, JsonElement payload, IServiceProvider? services, CancellationToken dispatchToken)
    {
        if (Live.Handlers is null || !Live.Handlers.TryGetValue(id, out var entry))
        {
            return false;
        }

        var (owner, handler) = entry;

        // The id said WHICH handler; the frame's own `type` says what it is carrying. An id names a
        // component's slot for that component's lifetime, so a frame that outlived its render usually
        // resolves to the same handler or to nothing at all — but a component that swapped what its
        // slot 0 renders (an input handler this frame, a click handler the next) still lands a stale
        // `input` message on a parameterless callback. A frame that cannot feed the handler it landed
        // on is a stale id by definition, and is answered exactly like one.
        if (!HandlerFrameShape.Accepts(payload, handler))
        {
            return false;
        }

        using var __dispatchScope = DispatchServicesScope.Push(services);

        // A handler runs outside any render walk, so there is no LiveRenderContext to read the culture
        // from — push it here, from the same service provider, so code the handler calls formats and
        // parses in the visitor's culture rather than the machine's.
        using var __cultureScope = Globalization.RaskCultureScope.PushFrom(services);

        using var linkedCts = LinkDispatchToken(owner, dispatchToken, out var eventTokenScope);
        using var __eventTokenScope = eventTokenScope;

        // The same services and cancellation, for the calls that take neither — `Cache.Remember(…)`,
        // `await Product.Create(model)`: they are cancelled when this component is, or the handler times out.
        using var __ambientServices = Ambient.Enter(services);
        using var __ambientToken = Ambient.Enter(linkedCts?.Token ?? owner.LifetimeToken);

        // Match Blazor: every event handler implicitly marks the registering component
        // dirty. Set BEFORE running so intermediate renders inside an async handler
        // (via InvokeWithRenderingAsync) already see the owner as dirty.
        owner.Live.StateDirty = true;
        try
        {
            return await DispatchAsync(handler, payload, owner).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ResolveHandlerBoundary(owner) is { } boundary)
        {
            // Route handler exceptions to the boundary that logically contains the handler.
            // When the owner is itself an ErrorBoundary (the common case: a button rendered
            // directly inside ErrorBoundary's Children — CurrentParent at registration time
            // is the boundary), THAT boundary catches. owner.Boundary would route one level
            // higher. For non-boundary owners (regular components), fall back to their
            // ancestor boundary. Without a boundary the exception bubbles so the dispatcher's
            // catch-and-log still fires.
            RaskDevToolsHook.Active?.ComponentFaulted(owner, ex, ErrorSource.Action, caught: true);
            boundary.Trip(ex, ErrorSource.Action);
            return true;
        }
    }

    // When the host supplied a cancellable dispatch token (a handler timeout is configured), make
    // owner.CancellationToken observe it during this handler by publishing a token linked with the
    // owner's lifetime token. With no timeout we push nothing — CancellationToken then resolves to
    // the plain lifetime token — so the common path stays allocation-free.
    private static CancellationTokenSource? LinkDispatchToken(
        Component owner, CancellationToken dispatchToken, out IDisposable? eventTokenScope)
    {
        if (!dispatchToken.CanBeCanceled)
        {
            eventTokenScope = null;
            return null;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.LifetimeToken, dispatchToken);
        eventTokenScope = DispatchEventTokenScope.Push(linked.Token);
        return linked;
    }

    // Runs one handler with the argument its shape asks for. Synchronous shapes run inline; the async ones are
    // pumped through InvokeWithRenderingAsync so mid-await state renders and a fault reaches the boundary.
    private async ValueTask<bool> DispatchAsync(Delegate handler, JsonElement payload, Component owner)
    {
        // A DOM event handler (e => … over MDN's MouseEvent, KeyboardEvent, …): the generated dispatch reads the
        // typed argument and runs a synchronous handler itself; an asynchronous one comes back as a Func<Task> and
        // takes the Func<Task> arm below — no await of its own, so this state machine stays the size it was.
        if (DomEventDispatch.TryInvoke(handler, payload, out var pendingDomEvent))
        {
            if (pendingDomEvent is null)
            {
                return true;
            }

            handler = pendingDomEvent;
        }

        if (TryInvokeSync(handler, payload))
        {
            return true;
        }

        if (handler is Func<IReadOnlyList<IRaskFile>, Task> filesHandler)
        {
            var files = FileListReader.Read(payload);
            try
            {
                await InvokeWithRenderingAsync(() => filesHandler(files)).ConfigureAwait(false);
            }
            finally { ReleaseFiles(files); }

            owner.Live.StateDirty = true;
            return true;
        }

        var pending = AsyncInvocation(handler, payload) ?? ReflectiveInvocation(handler);
        if (pending is not null)
        {
            await InvokeWithRenderingAsync(pending).ConfigureAwait(false);

            // The mid-await render inside InvokeWithRenderingAsync resets Live.StateDirty
            // to false when it walks the owner's subtree. Re-mark dirty here so the
            // dispatcher's post-handler render picks up state mutated AFTER the
            // mid-await window (e.g. an async validator's terminal message, or a
            // user lambda that ran on the continuation of an awaited Task).
            owner.Live.StateDirty = true;
        }

        return true;
    }

    private static bool TryInvokeSync(Delegate handler, JsonElement payload)
    {
        switch (handler)
        {
            case Action a:
                a();
                return true;
            case Action<string> a:
                a(ExtractString(payload, "value"));
                return true;
            case Action<IReadOnlyList<string>> a:
                a(ExtractStringList(payload));
                return true;
            case Action<FormData> a:
                a(FormData.FromJson(payload));
                return true;
            case Action<IReadOnlyList<IRaskFile>> a:
            {
                var files = FileListReader.Read(payload);
                try { a(files); }
                finally { ReleaseFiles(files); }

                return true;
            }
            // The shape an external component's callback arrives as (see Rask.External). Its
            // generated wrapper reads the argument out of the frame with code the generator
            // emitted, so an Action<int> or Action<SomeRecord> is fed without reflection and
            // without this switch needing a case per argument type.
            //
            // Necessary because there is no general Action<T> case and cannot be: T is only known
            // where the component is compiled. Without it such a delegate fell to the reflective
            // path, which DynamicInvokes with no arguments — so every argument-taking callback threw
            // TargetParameterCountException on its first click and the error boundary replaced the
            // page. It rendered, mounted, and died on use.
            case Action<JsonElement> ap:
                ap(payload);
                return true;
            default:
                return false;
        }
    }

    // The async shapes, as the call InvokeWithRenderingAsync pumps; null for any other. The argument is parsed
    // here, before the handler runs, as the synchronous shapes parse theirs.
    private static Func<Task>? AsyncInvocation(Delegate handler, JsonElement payload) => handler switch
    {
        Func<Task> f => f,
        Func<string, Task> f => Bind(f, ExtractString(payload, "value")),
        Func<IReadOnlyList<string>, Task> f => Bind<IReadOnlyList<string>>(f, ExtractStringList(payload)),
        Func<FormData, Task> f => Bind(f, FormData.FromJson(payload)),
        Func<JsonElement, Task> f => Bind(f, payload),
        _ => null,
    };

    private static Func<Task> Bind<T>(Func<T, Task> handler, T argument) => () => handler(argument);

    // Parameterless delegate shapes outside the fast-path lists can still arrive through a typed handler
    // slot (e.g. a method group typed Func<Task<T>> or Func<ValueTask> wired to a drag handler). Invoke
    // reflectively, and if the result is an awaitable, hand it back to be pumped through the render path so
    // exceptions reach the ErrorBoundary and post-await state changes re-render — matching the explicit
    // Func<…, Task> cases. Without this, a returned Task is fire-and-forget: a fault is unobserved and
    // post-await mutations never render.
    private static Func<Task>? ReflectiveInvocation(Delegate handler)
    {
        Task? pending = handler.DynamicInvoke() switch
        {
            Task t => t,
            ValueTask vt => vt.AsTask(),
            _ => null
        };
        return pending is null ? null : () => pending;
    }

    private async Task InvokeWithRenderingAsync(Func<Task> invoke)
    {
        var handle = RenderHandle;
        if (handle is null)
        {
            await invoke().ConfigureAwait(false);
            return;
        }

        var prev = SynchronizationContext.Current;
        var ctx = new HandlerSyncContext(handle.RenderInScopeAsync);
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            var userTask = invoke();
            if (!userTask.IsCompleted)
            {
                // Suspend HandlerSyncContext for the duration of the render-and-send. Kestrel's
                // WebSocket.SendAsync has internal awaits that don't all use ConfigureAwait(false),
                // so any leaking ambient sync context becomes the target for its flush
                // continuation. With HandlerSyncContext as the target, that continuation Posts a
                // RunWithRendersAsync, which fires *another* render-and-send on the same socket —
                // a recursive render chain that races the in-flight one, the WS lock, and the
                // user's still-pending async work. Restoring prev for the render call confines
                // HandlerSyncContext to the user-code window where it's actually meaningful.
                SynchronizationContext.SetSynchronizationContext(prev);
                try
                {
                    await handle.RenderInScopeAsync().ConfigureAwait(false);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(ctx);
                }
            }

            await userTask.ConfigureAwait(false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
            await ctx.DrainAsync().ConfigureAwait(false);
        }
    }

    private static string ExtractString(JsonElement payload, string property)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var v))
        {
            return string.Empty;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>
    ///     The whole selection a <c>&lt;select multiple&gt;</c> reported, from the frame's <c>values</c>
    ///     array. Falls back to the single <c>value</c> so the handler still sees the user's pick when
    ///     the array is absent — a single-value control wired to a list handler, or a browser holding a
    ///     cached client from a deploy that predates the array.
    /// </summary>
    private static string[] ExtractStringList(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("values", out var v)
            && v.ValueKind == JsonValueKind.Array)
        {
            var length = v.GetArrayLength();
            if (length == 0)
            {
                return [];
            }

            var picked = new string[length];
            var i = 0;
            foreach (var item in v.EnumerateArray())
            {
                picked[i++] = item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : string.Empty;
            }

            return picked;
        }

        var single = ExtractString(payload, "value");
        // "" is what an empty select reports, and it is not a selection — reporting it as one option
        // named "" would make "nothing picked" indistinguishable from "picked the blank option".
        return single.Length == 0 ? [] : new[] { single };
    }

    private static void ReleaseFiles(IReadOnlyList<IRaskFile> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        FileListReader.ResolveBackend()?.Release(files);
    }

    /// <summary>
    ///     Handler-id bookkeeping, hung off <see cref="LiveState.HandlerState" /> so a component that
    ///     renders no handler pays one null reference rather than a field per role. The render root
    ///     uses the first two members (it owns the id source); a component that renders handlers uses
    ///     the rest (its own slot table). See <see cref="RegisterHandler(Delegate, Component)" />.
    ///     <para>
    ///         A slot id means something only within the sequence that minted it, so a component that
    ///         turns up under a different root drops its table and re-mints — see
    ///         <see cref="MintedUnder" />. Its ids are not stable across that move, which is correct:
    ///         they were never ids the new root had issued.
    ///     </para>
    /// </summary>
    private sealed class HandlerState
    {
        // ---- render root ----

        /// <summary>
        ///     The next number this root will hand out. Monotonic for the root's whole life: numbers are
        ///     never reset and never reused, so a stale in-flight event cannot be redirected onto a
        ///     component that took a freed number.
        /// </summary>
        public int NextNumber;

        /// <summary>
        ///     Identifies the current root render, so each component resets its slot counter exactly
        ///     once per frame. Drawn from a process-wide source rather than counted per root, because a
        ///     component reached from two different roots must never mistake one root's generation for
        ///     the other's and skip its reset.
        /// </summary>
        public long Generation;

        /// <summary>
        ///     How many times the browser was sent a page whose handlers had moved. An event says which of those
        ///     pages it was read from, counted from <see cref="Floor" />.
        /// </summary>
        public int Version;

        /// <summary>The <see cref="Version" /> the browser's document was, which it counts from as zero.</summary>
        public int Floor;

        /// <summary>A walk since the last page was sent put a different handler in a slot.</summary>
        public bool Moved;

        // ---- a component that renders handlers ----

        /// <summary>
        ///     The <see cref="HandlerState" /> of the root whose sequence minted this component's slot
        ///     ids. Compared by reference on every registration: a component that turns up under a
        ///     different root re-mints, because ids from one root's sequence mean nothing in another's.
        /// </summary>
        public HandlerState? MintedUnder;

        /// <summary>The generation in which <see cref="LocalCount" /> was last reset.</summary>
        public long Stamp;

        /// <summary>Slots this component has used so far in the generation named by <see cref="Stamp" />.</summary>
        public int LocalCount;

        /// <summary>
        ///     The id for slot 0 — a scalar, so the common single-handler component never allocates an
        ///     array. Minted on first use and then held for the component's lifetime.
        /// </summary>
        public string? Slot0Id;

        /// <summary>
        ///     Ids for slots 1.. (index <c>i</c> → slot <c>i+1</c>), grown geometrically and allocated
        ///     only when a second handler appears. A null entry is a slot never yet reached.
        /// </summary>
        public string[]? RestIds;
    }
}
