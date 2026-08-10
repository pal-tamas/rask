namespace Rask.Core;

/// <summary>
///     PROTOTYPE — the runtime the generated builder setters call into (see
///     <c>RaskBuilderSetters*.g.cs</c>).
/// </summary>
/// <remarks>
///     Public because the setters are emitted into the GLOBAL namespace of every consuming assembly —
///     an extension method is only found when its namespace is in scope, and the global namespace
///     encloses all — so they cannot reach an internal member of Rask.Core. The generator also fills a
///     second half of this class in (<c>RaskBuilderReset.g.cs</c>): the reset routines for the shared
///     <see cref="Element" />/<see cref="Component" /> surface, which is why it is <c>partial</c>.
/// </remarks>
public static partial class BuilderRuntime
{
    /// <summary>
    ///     Records a prop change on <paramref name="target" /> when the setter's value differs from the
    ///     one already there, so the deferred commit reports <c>propsChanged: true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the setter-chain half of what the generated factory does in one shot: snapshot
    ///         every folding prop, assign them all, then <c>NotifyParameters(__c, __propsChanged)</c>.
    ///         A chain has no natural end, so the "did anything change" fold is accumulated here as it
    ///         happens and the notification is deferred to the end of the parent's <c>Render()</c>.
    ///     </para>
    ///     <para>
    ///         <see cref="EqualityComparer{T}" /> matches the factory exactly: reference equality for a
    ///         reference type that does not override <c>Equals</c>, structural for primitives. The
    ///         factory's exclusions are honoured by the generator instead of here — <c>Key</c> (a
    ///         reconciliation identity, not a reactive prop), auto-wrapped callbacks and raw delegates
    ///         (a fresh closure every render, so folding them would force a change every frame) and
    ///         carrier props simply get a setter with no <c>Track</c> call.
    ///     </para>
    ///     <para>
    ///         The value it compares against is the one the PREVIOUS render left on the component, not a
    ///         freshly defaulted one: a folding prop is reset at the end of the render (see
    ///         <see cref="Pending" />), never before the chain runs, precisely so this comparison keeps
    ///         the factory's meaning.
    ///     </para>
    /// </remarks>
    public static void Track<TValue>(Component target, TValue oldValue, TValue newValue)
    {
        if (!EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
        {
            target.MarkEntryPropsChangedInternal();
        }
    }

    /// <summary>Marks <paramref name="target" /> as prop-changed unconditionally.</summary>
    /// <remarks>
    ///     The generated reset routines have already compared the prop to its default before calling
    ///     this, so re-running <see cref="Track{TValue}" />'s comparison would be pure waste.
    /// </remarks>
    public static void MarkChanged(Component target) => target.MarkEntryPropsChangedInternal();

    /// <summary>
    ///     The bit a folding prop of the shared <see cref="Element" />/<see cref="Component" /> surface
    ///     may claim. Own (per-component) props are numbered from here up.
    /// </summary>
    /// <remarks>
    ///     Fixed rather than "however many the shared surface currently has" so a component compiled
    ///     against one version of Rask.Core cannot have its own bits collide with a shared prop added in
    ///     a later one. The generator falls back to an eager reset for any prop that does not fit.
    /// </remarks>
    public const int OwnPendingBit = 16;

    // ---- Pending resets --------------------------------------------------------------------------
    //
    // A generated FACTORY assigns EVERY parameter each render, so a prop the caller omitted this render
    // is put back to its default and cannot survive from the last one. A setter chain only writes the
    // props it names, and the entry hands back the same instance — so without this, `Div.Id("x")` on one
    // render and `Div` on the next still renders `id="x"`.
    //
    // The reset cannot simply run when the entry is created: `Track` above compares the incoming value
    // to the one already on the component, and defaulting it first would make every constant prop look
    // like a change on every frame (defeating the render cache for anything built by an entry). So the
    // entry instead marks its folding props PENDING, each setter clears its own bit as it writes, and
    // whatever is still pending when the parent's Render() returns is reset then — with the previous
    // value still in place, so the fold stays exactly what the factory would have reported.
    //
    // Non-folding props (raw delegates, carriers, Key) skip all of this: they are reset eagerly by the
    // entry, because they never participate in the fold and so cannot disturb it.

    internal readonly record struct EntrySlot(
        Component Parent,
        Component Target,
        Action<Component, ulong> Reset,
        ulong Pending);

    // Thread-static rather than a field on the component or its LiveState: LiveState is allocated per
    // node on a mounted page, where one extra reference costs ~56 KB per 1,000 rows (see the note on
    // LiveState.Cached). The slots only have to survive from the entry to the end of the enclosing
    // Render(), which is a synchronous, strictly nested window, so a per-thread stack holds them for
    // free and is reused across renders — a tree built entirely from factories never touches it.
    [ThreadStatic]
    private static List<EntrySlot>? _slots;

    internal static void PushSlot(Component parent, Component target, Action<Component, ulong> reset, ulong pending)
        => (_slots ??= new List<EntrySlot>()).Add(new EntrySlot(parent, target, reset, pending));

    // Scratch for the deferred commit's snapshot of a parent's child map (Component.CommitEach). Same
    // discipline and the same reasons as the slot stack above: per-thread so it costs no field on
    // LiveState, reused across renders so the steady state allocates nothing, and used as a stack —
    // each commit appends its own range and truncates back — so a lifecycle hook that re-enters another
    // component's render nests cleanly instead of clobbering the frame below it.
    [ThreadStatic]
    private static List<Component>? _commitBuffer;

    internal static List<Component> CommitBuffer => _commitBuffer ??= new List<Component>();

    /// <summary>Clears <paramref name="bit" /> — the prop it stands for was written by the chain.</summary>
    /// <remarks>
    ///     A backwards scan, not a lookup: a setter runs immediately after the entry that produced its
    ///     receiver, so the match is the top of the stack in every chain. It finds nothing at all when
    ///     the receiver came from a FACTORY (`Div().Class("x")`, valid during the migration) — that
    ///     component is fully re-assigned by its factory and must not be reset here.
    /// </remarks>
    public static void Written(Component target, ulong bit)
    {
        var slots = _slots;
        if (slots is null)
        {
            return;
        }

        for (var i = slots.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(slots[i].Target, target))
            {
                var slot = slots[i];
                slots[i] = slot with { Pending = slot.Pending & ~bit };
                return;
            }
        }
    }

    /// <summary>The prop bits still pending on <paramref name="target" />, for tests and diagnostics.</summary>
    internal static ulong Pending(Component target)
    {
        var slots = _slots;
        if (slots is not null)
        {
            for (var i = slots.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(slots[i].Target, target))
                {
                    return slots[i].Pending;
                }
            }
        }

        return 0UL;
    }

    // Runs the reset for every entry `parent` built during the Render() that just returned.
    //
    // In practice a parent's slots are a contiguous run at the top of the stack — a child's own Render()
    // happens later, during serialization, by which time this has already popped — so this normally
    // stops at the first slot from the end. It keeps scanning anyway rather than treating that as an
    // invariant: a Render() that re-enters another component's render (a nested ToHtml(), say) could
    // bury one of this parent's slots under someone else's, and a slot left behind is both a stale prop
    // that never gets reset and a leak on a stack that is only ever popped by its owner. The scan runs
    // only while entries are in flight — a tree built entirely from factories never allocates the list
    // and returns on the null check above.
    internal static void DrainSlots(Component parent)
    {
        var slots = _slots;
        if (slots is null)
        {
            return;
        }

        // One forward pass: reset what this parent owns (in creation order, the order the chains ran)
        // and compact everything else down. No allocation, and no reliance on where the slots sit.
        var write = 0;
        for (var read = 0; read < slots.Count; read++)
        {
            var slot = slots[read];
            if (ReferenceEquals(slot.Parent, parent))
            {
                slot.Reset(slot.Target, slot.Pending);
                continue;
            }

            slots[write++] = slot;
        }

        slots.RemoveRange(write, slots.Count - write);
    }

    /// <summary>A component with nothing to reset — no own props and not an <see cref="Element" />.</summary>
    public static void ResetNothing(Component target, ulong pending)
    {
        _ = target;
        _ = pending;
    }

    // ---- Entry construction ----------------------------------------------------------------------
    //
    // The bodies live here rather than on Component because the generator emits ONE canonical entry per
    // component into a public `RaskEntries{Assembly}` class in the global namespace (see
    // `RaskBuilderEntryHost.g.cs`), and every per-component forwarder — this assembly's and every
    // consumer's — routes through it. A static class cannot reach Component's `protected static`
    // helpers, so the reachable half has to be public and non-derived. Component's `Entry`/`EntryDi`/
    // `EntryBound` are thin forwarders onto these and stay the documented, protected surface.

    /// <inheritdoc cref="Component.Entry{T}" />
    public static T Entry<T>(
        Action<Component> reset,
        Action<Component, ulong> pendingReset,
        ulong pending)
        where T : Component, new()
    {
        if (Live.LiveRenderContext.Current is not { } ctx)
        {
            return new T();
        }

        var component = ctx.GetOrCreateEntry<T>(static _ => new T(), pendingReset, pending);
        reset(component);
        return component;
    }

    /// <inheritdoc cref="Component.EntryDi{T}" />
    public static T EntryDi<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        Action<Component> reset,
        Action<Component, ulong> pendingReset,
        ulong pending)
        where T : Component
    {
        if (Live.LiveRenderContext.Current is { } ctx)
        {
            var component = ctx.GetOrCreateEntry<T>(
                static sp => Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<T>(sp),
                pendingReset,
                pending);
            reset(component);
            return component;
        }

        throw new InvalidOperationException(
            $"Component '{typeof(T)}' has no parameterless constructor; it can only be instantiated "
            + "inside a LiveRenderContext (e.g. via MapRask<TApp>).");
    }

    /// <inheritdoc cref="Component.EntryRequired{T}" />
    public static T EntryRequired<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        Action<Component> reset,
        Action<Component, ulong> pendingReset,
        ulong pending)
        where T : Component
    {
        if (Live.LiveRenderContext.Current is not { } ctx)
        {
            return Activator.CreateInstance<T>();
        }

        var component = ctx.GetOrCreateEntry<T>(
            static _ => Activator.CreateInstance<T>(), pendingReset, pending);
        reset(component);
        return component;
    }
}
