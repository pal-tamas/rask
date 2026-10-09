using System.Buffers;
using System.Runtime.CompilerServices;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    // Primary children indexer. `Div()[Span(...), "hi"]` is the call shape: literal lists of
    // components/strings (each implicitly a Component via the converters above). Overload
    // resolution prefers this `params Component[]` form over the IEnumerable<…> variant below —
    // the compiler emits a single `new Component[N]{ … }` and we assign it directly, no copy.
    // [OverloadResolutionPriority] is load-bearing, not tuning. `string` implements IEnumerable, so
    // without it the `params object?[]` overload below — applicable in NORMAL form, which beats this
    // one's EXPANDED form — would capture `Div["hi"]` and render one child per character. The priority
    // keeps every call site that the typed overloads can serve on the typed path, so the loose overload
    // is reached only when nothing else binds.
    [OverloadResolutionPriority(1)]
    public Component this[params Component?[] children]
    {
        get
        {
            Children = children;
            return this;
        }
    }

    // Single-arg enumerable form: a `List<Component>`, a `.Select(...)` LINQ projection, or any
    // pre-built `IEnumerable<Component>`. The compiler picks this over the `params Component[]`
    // overload only when the arg is a single sequence that isn't already a `Component[]`.
    [OverloadResolutionPriority(1)]
    public Component this[IEnumerable<Component?> children]
    {
        get
        {
            // Materialise a *lazy* sequence (a `yield`/LINQ pipeline that hasn't been evaluated)
            // right here, during Render. A component may be built by a factory, and those factories
            // must run NOW — inside the owning component's render walk, where child-reuse bookkeeping
            // (GetOrCreateChild's position map + PreviousChildren swap) is live — not later during
            // serialization when that state is gone. Deferring would recreate any embedded component
            // every render and silently drop its state (e.g. a demo mounted from a yield-built list).
            // Already-materialised collections (Component?[]/List<Component?>/…) ran their factories
            // when the caller built them, so they pass through without a copy.
            Children = children is IReadOnlyCollection<Component?> ? children : children.ToArray();
            return this;
        }
    }

    /// <summary>
    ///     Children that a typed list cannot express: a projection of chains, or literals and a
    ///     projection mixed in one list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A chain that ends at a STEP has the type <c>Build&lt;T&gt;</c>, not <c>Component</c> — the
    ///         implicit conversion is what makes it a component at the call site, and a user-defined
    ///         conversion never lifts through <c>IEnumerable&lt;&gt;</c>. So
    ///         <c>rows.Select(r =&gt; Badge.Label(r))</c> yields <c>IEnumerable&lt;Build&lt;Badge&gt;&gt;</c>,
    ///         which the typed overloads cannot accept. The fix belongs here rather than at every call
    ///         site, where it would otherwise be a cast or a named method per list.
    ///     </para>
    ///     <para>
    ///         The parameter is <c>object?</c> rather than a sequence because that also makes the mixed
    ///         list expressible — a spread element (<c>..</c>) is only grammar inside a collection
    ///         expression, never in an argument list, so flattening is what lets a projection sit beside
    ///         literal children:
    ///         <c>Div["Showing ", rows.Select(r =&gt; Row.For(r)), " of ", total]</c>.
    ///     </para>
    ///     <para>
    ///         An INDEXER cannot be generic in C#, and a C# 14 extension block cannot declare one
    ///         (CS9282), so a type-safe <c>this[IEnumerable&lt;Build&lt;T&gt;&gt;]</c> is not expressible
    ///         in the language. What this overload gives up is the compile error on an element that
    ///         cannot be a child; it throws while rendering instead, naming the type.
    ///     </para>
    /// </remarks>
    public Component this[params object?[] children]
    {
        get
        {
            // Materialised to Component?[] rather than kept lazy, for the same reason the enumerable
            // overload materialises: embedded factories must run inside the owning component's render
            // walk. The array shape also keeps ChildrenArray's serializer fast-path below.
            var flat = new ChildBuffer(children.Length);
            try
            {
                foreach (var child in children)
                {
                    AddChild(ref flat, child);
                }

                Children = flat.ToArray();
            }
            finally
            {
                flat.Dispose();
            }

            return this;
        }
    }

    // Mirrors the implicit operators above — anything spellable as a child directly is spellable
    // inside a sequence — plus chains, plus nested sequences. Anything else is a mistake the typed
    // overloads would have caught, so it throws rather than rendering ToString() garbage.
    private static void AddChild(ref ChildBuffer flat, object? child)
    {
        // A string IS a sequence of chars, and flattening it would turn one text node into one node per
        // character; a component is a child as it stands.
        if (child is System.Collections.IEnumerable sequence and not string and not Component)
        {
            foreach (var item in sequence)
            {
                AddChild(ref flat, item);
            }

            return;
        }

        flat.Add(ToChild(child));
    }

    // The flattened children, collected in a pooled array and copied once to their exact size — a List plus
    // its ToArray would allocate the list, every regrowth of it, and the copy. Rented per call, not shared:
    // a lazy sequence runs its factories while it is flattened, and those can land back in this indexer.
    private struct ChildBuffer(int capacity) : IDisposable
    {
        private Component?[] _items = ArrayPool<Component?>.Shared.Rent(Math.Max(16, capacity));
        private int _count;

        public void Add(Component? child)
        {
            if (_count == _items.Length)
            {
                var bigger = ArrayPool<Component?>.Shared.Rent(_items.Length * 2);
                _items.AsSpan(0, _count).CopyTo(bigger);
                ArrayPool<Component?>.Shared.Return(_items, true);
                _items = bigger;
            }

            _items[_count++] = child;
        }

        public readonly Component?[] ToArray() => _items.AsSpan(0, _count).ToArray();

        // Cleared on return: the pool would otherwise keep this render's components alive.
        public void Dispose() => ArrayPool<Component?>.Shared.Return(_items, true);
    }

    // A chain is a Component, so the Component arm needs no unwrapping.
    private static Component? ToChild(object? child) => child switch
    {
        null => null,
        Component component => component,
        string text => text,
        int value => value,
        long value => value,
        double value => value,
        float value => value,
        decimal value => value,
        bool value => value,
        char value => value,
        Guid value => value,
        DateOnly value => value,
        TimeOnly value => value,
        DateTime value => value,
        DateTimeOffset value => value,
        TimeSpan value => value,
        _ => throw new InvalidOperationException(
            $"'{child.GetType()}' cannot be a child: it is not a component, not a chain, and has no " +
            "text representation. Render it through a component, or convert it to a string first."),
    };

    // Serializer fast-path. The hot indexer overloads leave `Children` holding a `Component?[]`.
    // Exposing the raw array lets the render walk iterate by index instead of `foreach`-ing the
    // `IEnumerable<Component?>` interface — which boxes a `SZGenericArrayEnumerator<Component?>`
    // (~32 B) per child-bearing element, every render. Returns null for the List/LINQ-pipeline
    // backings, which fall back to the virtual `RenderChildren()` walk. No component in Rask.Core
    // overrides `RenderChildren`, so for Element subclasses this array is exactly what it yields.
    internal Component?[]? ChildrenArray => Children as Component?[];

    // GetOrCreateChild counts positions up from 0, so this can never collide with one.
    private const int AdoptedChildPosition = int.MaxValue;

    /// <summary>
    ///     Registers an already-constructed <paramref name="child" /> as this component's child, outside
    ///     the positional <see cref="GetOrCreateChild{T}" /> path, and gives it a render handle.
    /// </summary>
    /// <remarks>
    ///     For a render root that forwards to a component it did not build through a generated factory —
    ///     which is every component handed to <c>Page.Render</c> as an object rather than produced by
    ///     the factory during the render. Those never reach <c>GetOrCreate</c>, so without adoption they
    ///     serialize but are invisible to the alive-set walk (no <c>OnRendered</c>, no <c>OnUnmount</c>)
    ///     and have no handle to re-render through when an asynchronous lifecycle hook completes.
    ///     <para>
    ///     Deliberately not <see cref="GetOrCreateChild{T}" />: that path's reuse branch clears the
    ///     instance's <see cref="Children" />, which would delete the subtree of a tree built at the call
    ///     site (<c>Div()[Span()]</c>) on its second render, and would put the instance's identity under
    ///     positional-cache rules. Adoption keeps the caller's object exactly as it was handed over.
    ///     </para>
    /// </remarks>
    internal void AdoptChild(Component child, IRenderHandle? handle)
    {
        if (_live?.Children is { } existing)
        {
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
            foreach (var registered in existing.Values)
#pragma warning restore S3267
            {
                // Already registered this frame — it came from a generated factory's GetOrCreate, which
                // has done both halves of this itself.
                if (ReferenceEquals(registered, child))
                {
                    return;
                }
            }
        }

        child.RenderHandle ??= handle;

        // Counting DOWN from AdoptedChildPosition, so a parent that adopts several instances of one type — a
        // row of plugin widgets — keeps each of them rather than the last overwriting the rest.
        var children = Live.Children ??= new Dictionary<(Type, int), Component>();
        var position = AdoptedChildPosition;
        while (children.ContainsKey((child.GetType(), position)))
        {
            position--;
        }

        children[(child.GetType(), position)] = child;
    }

    // Whether this component's lifecycle has started — read without allocating the live state, because the
    // serializer asks it of every component it walks.
    internal bool HasInitializedInternal => _live is { HasInitialized: true };

    // Whether the walk has to register this component with its parent: never started (nothing registered it),
    // or registered by the walk itself last time (nothing else will register it again). Read without allocating
    // the live state; one field read for every chain-built component.
    internal bool NeedsWalkAdoptionInternal => _live is not { HasInitialized: true, AdoptedByWalk: false };

    internal void MarkAdoptedByWalkInternal() => Live.AdoptedByWalk = true;

    internal T GetOrCreateChild<T>(
        Func<IServiceProvider, T> factory,
        IServiceProvider? services,
        IRenderHandle? handle) where T : Component
    {
        var key = (typeof(T), Live.ChildPositions++);
        Live.LastChildSlot = key;
        T instance;
        // A type this parent identifies by Key is NOT identified by its ordinal among all the children
        // (#685): recycling the instance that happens to sit at this ordinal would hand a brand-new key
        // whichever item used to be here, state and all. It gets the instance the next UNKEYED child of
        // the type held, which is right if no Key step follows and is given back if one does (#1215).
        if (Live.KeyedTypes is not null && Live.KeyedTypes.TryGetValue(typeof(T), out var siblings))
        {
            if (siblings.NextHeld() is T held)
            {
                instance = held;
                instance.Children = null;
            }
            else
            {
                instance = siblings.Spare as T ?? NewChild(factory, services);
                siblings.Spare = null;
                siblings.Hold(instance);
            }
        }
        else if (Live.PreviousChildren is not null && Live.PreviousChildren.TryGetValue(key, out var prev) &&
                 prev is T t)
        {
            instance = t;
            // The factory re-applies every factory-param property each render, but Children is
            // set by the `[...]` indexer AFTER the factory returns — and a childless element
            // (no indexer) never sets it. Reset it here so a reused instance can't inherit the
            // previous occupant's children. Without this, a structural move that shifts the
            // positional cache onto a former-parent instance (e.g. an empty drop-zone div lands
            // on an old card's slot) keeps that parent's subtree wired in, producing a cyclic
            // tree and a stack overflow when serialized. The indexer overwrites this for any
            // element that does declare children.
            instance.Children = null;
        }
        else
        {
            // Pass through whatever IServiceProvider the LiveRenderContext was given —
            // possibly null. The generated factory closure for non-DI components ignores
            // the parameter, so null is fine; DI-ctor closures (ActivatorUtilities) will
            // surface their own NRE if asked to resolve against a null provider.
            instance = NewChild(factory, services);
        }

        instance.RenderHandle ??= handle;
        (Live.Children ??= new Dictionary<(Type, int), Component>())[key] = instance;
        return instance;
    }

    internal Component GetOrCreateChild(
        Type type,
        Func<IServiceProvider, Component> factory,
        IServiceProvider? services,
        IRenderHandle? handle)
    {
        var key = (type, Live.ChildPositions++);
        Component instance;
        if (Live.PreviousChildren is not null && Live.PreviousChildren.TryGetValue(key, out var prev) &&
            prev.GetType() == type)
        {
            instance = prev;
            // See the generic overload: clear children on reuse so a childless element can't
            // inherit a former occupant's subtree after a positional-cache shift.
            instance.Children = null;
        }
        else
        {
            instance = factory(services!);

            // Record the creating parent once at creation — see the generic overload.
            if (instance is Forms.IFormControl fc)
            {
                Forms.BindingConsumerRegistry.Record(fc, this);
            }
        }

        instance.RenderHandle ??= handle;

        (Live.Children ??= new Dictionary<(Type, int), Component>())[key] = instance;
        return instance;
    }

    private T NewChild<T>(Func<IServiceProvider, T> factory, IServiceProvider? services) where T : Component
    {
        var instance = factory(services!);

        // `this` is the creating parent (CurrentParent when the factory ran) — the provider whose
        // Render() authored this control. A form control records it once at creation so a bound
        // two-way write outside a Form can re-render the provider's derived UI (see
        // Forms/BindingConsumerRegistry). The creator is stable across frames, so a reused instance
        // keeps its entry — no work on the steady-state render path.
        if (instance is Forms.IFormControl control)
        {
            Forms.BindingConsumerRegistry.Record(control, this);
        }

        return instance;
    }
}
