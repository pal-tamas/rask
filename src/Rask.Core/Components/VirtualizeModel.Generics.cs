using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rask.Core.Virtualization;

namespace Rask.Core.Components;

// Hand-written generic entry point for VirtualizeModel — the ONE on the surface, because a chain
// infers its type argument from the step that OPENS it and T here comes from the RENDER delegate.
// Captures T in a closure so the runtime side stays type-erased: no reflection, no DynamicInvoke,
// trim-safe.
//
// The typed call site:
//   Virtualize.Items<Person>(Render: ctx => Div(...)[..ctx.VisibleItems.Select(...)], Items: people)
// becomes a forward to the generated non-generic factory:
//   VirtualizeModel(Items: ..., ItemSize: 32, Body: state => Render(new VirtualizationContext<Person>(state)))
//
// It lives in its OWN class rather than in `Generated` (#684). While it sat in `Generated` it was
// reachable by simple name only because that whole class is globally imported — which is exactly what
// #681's follow-up wants to stop doing. Moving it out as `VirtualizeModel` did not work either: the
// simple name also names the COMPONENT's chain entry, an inherited member beats a `using static`
// import in simple-name lookup, and every call site failed with CS1744 as overload resolution landed
// on the entry. Renaming the method to `Items` is what removes the collision, so this class can be
// static-imported on its own without dragging the factory class along with it.
public static partial class Virtualize
{
    // Wrapper-cache: dedup erased-provider closures per typed user delegate. Without this,
    // every VirtualizeModel<T> factory call would allocate a fresh `async req => …` wrapper, the
    // generated non-generic factory's per-property reference compare would flag ItemsProvider
    // as changed every render, and VirtualizeModel.OnPropsChanged would treat that as a data-source
    // swap and reset the cache. ConditionalWeakTable lets the wrapper live exactly as long as
    // the typed delegate the user supplied.
    private static readonly ConditionalWeakTable<Delegate, Delegate> _erasedProviderCache = new();

    /// <summary>
    ///     Renders only the rows near the viewport, so a list of any length costs the same to render.
    ///     This is the typed call site: <c>T</c> is inferred from <paramref name="Items" /> (or from
    ///     <paramref name="ItemsProvider" />), and <paramref name="Render" /> receives a context whose
    ///     visible items are already that type — no cast, no reflection.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="Render">Renders the visible window, given a typed virtualization context.</param>
    /// <param name="Items">
    ///     The full set of rows. For a set too large — or too remote — to hold in memory, supply
    ///     <paramref name="ItemsProvider" /> instead.
    /// </param>
    /// <param name="ItemsProvider">
    ///     Fetches one window of rows on demand. Pass this <b>or</b> <paramref name="Items" />.
    /// </param>
    /// <param name="ItemSize">
    ///     Each row's height in pixels. The scroll maths assumes every row is exactly this tall, so a
    ///     wrong value shows up as drift while scrolling.
    /// </param>
    /// <param name="OverscanCount">
    ///     How many extra rows to render beyond the viewport, trading a little work for fewer blank
    ///     rows during a fast scroll.
    /// </param>
    /// <param name="InitialClientHeight">
    ///     The viewport height to assume for the first render, before the browser has reported the
    ///     real one.
    /// </param>
    [UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "T flows through here only via closures over user-supplied delegates. " +
                        "No reflection, no DynamicInvoke; the typed → erased projections are static casts.")]
    public static VirtualizeModel Items<T>(
        Func<VirtualizationContext<T>, Component> Render,
        IEnumerable<T>? Items = null,
        Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<T>>>? ItemsProvider = null,
        int ItemSize = 32,
        int OverscanCount = 3,
        int InitialClientHeight = 400)
    {
        ArgumentNullException.ThrowIfNull(Render);

        // Wrap the typed render fragment into a closure over a VirtualizationState. Body
        // changes per render are fine — VirtualizeModel.OnPropsChanged doesn't reset state on
        // Body swaps, only on Items/ItemsProvider swaps.
        Func<VirtualizationState, Component> body =
            state => Render(new VirtualizationContext<T>(state));

        Delegate? erasedProvider = null;
        if (ItemsProvider is not null)
        {
            erasedProvider = _erasedProviderCache.GetValue(
                ItemsProvider,
                static typed => WrapTypedProvider<T>(
                    (Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<T>>>)typed));
        }

        // Built through the CHAIN, reached by naming the assembly's entry class outright: this class is
        // not a markup host, so the entry is not in scope by simple name here. Qualifying is the same
        // escape the Bs controls use for the `<label>` entry their own `Label` property hides.
        // ItemSize opens it: non-nullable with no initializer, so it is a REQUIRED step (RASK001) and
        // the component does not exist until it has been supplied. Everything else is a setter.
        return global::RaskEntriesRask_Core.VirtualizeModel
            .ItemSize(ItemSize)
            .Body(body)
            .Items(Items)
            .ItemsProvider(erasedProvider)
            .OverscanCount(OverscanCount)
            .InitialClientHeight(InitialClientHeight);
    }

    private static Func<ItemsProviderRequest, ValueTask<ItemsProviderResultErased>> WrapTypedProvider<T>(
        Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<T>>> typed) =>
        async req =>
        {
            var result = await typed(req).ConfigureAwait(false);
            var boxed = new object?[result.Items.Count];
            for (var i = 0; i < boxed.Length; i++)
            {
                boxed[i] = result.Items[i];
            }

            return new ItemsProviderResultErased(boxed, result.TotalItemCount);
        };
}
