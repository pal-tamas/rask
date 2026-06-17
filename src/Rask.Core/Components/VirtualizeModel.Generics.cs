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

        return VirtualizeModel(
            Body: body,
            Items: Items,
            ItemsProvider: erasedProvider,
            ItemSize: ItemSize,
            OverscanCount: OverscanCount,
            InitialClientHeight: InitialClientHeight);
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
