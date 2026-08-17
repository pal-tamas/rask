using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rask.Core.Virtualization;

namespace Rask.Core.Components;

// Hand-written generic factory for VirtualizeModel. Lives in the same `partial class Generated`
// that ComponentFactoryGenerator emits the non-generic factory into. Captures T in a closure
// so the runtime side stays type-erased — no reflection, no DynamicInvoke, trim-safe.
//
// The typed call site:
//   VirtualizeModel<Person>(Items: people, ItemSize: 32, Render: ctx => Div(...)[..ctx.VisibleItems.Select(...)])
// becomes a forward to the generated non-generic factory:
//   VirtualizeModel(Items: ..., ItemSize: 32, Body: state => Render(new VirtualizationContext<Person>(state)))
public static partial class Generated
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
    public static VirtualizeModel VirtualizeModel<T>(
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

        // Built through the CHAIN, reached by naming the assembly's entry class outright: this facade
        // lives in `Generated`, which already declares a `VirtualizeModel` method, so the inherited entry
        // is not in scope here and marking this class a markup host would collide with it (CS0102).
        // Same escape the Bs controls use for the `<label>` entry their own `Label` property hides.
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
