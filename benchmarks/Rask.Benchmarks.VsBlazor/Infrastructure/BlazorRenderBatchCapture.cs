using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.Benchmarks.VsBlazor.Infrastructure;

/// <summary>
///     Subclass of <see cref="Renderer" /> that captures the byte count of each
///     <see cref="RenderBatch" /> as it would be serialized over a Blazor Server SignalR
///     circuit. Bytes are measured inside <see cref="UpdateDisplayAsync" /> (synchronously,
///     before returning) so the pooled arrays underlying the batch are still valid.
///     <para>
///         The serialization itself happens in <see cref="BlazorBatchByteSizer.Measure" />,
///         which reflects on the internal <c>RenderBatchWriter</c> type from
///         <c>Microsoft.AspNetCore.Components.Server</c>.
///     </para>
/// </summary>
public sealed class BlazorRenderBatchCapture : Renderer
{
    private static readonly IServiceProvider EmptyServices = new ServiceCollection().BuildServiceProvider();

    public BlazorRenderBatchCapture()
        : this(EmptyServices, NullLoggerFactory.Instance)
    {
    }

    public BlazorRenderBatchCapture(IServiceProvider services, ILoggerFactory loggerFactory)
        : base(services, loggerFactory)
    {
    }

    public long LastBatchByteCount { get; private set; }

    public override Dispatcher Dispatcher { get; } = new InlineDispatcher();

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        LastBatchByteCount = BlazorBatchByteSizer.Measure(in renderBatch);
        return Task.CompletedTask;
    }

    protected override void HandleException(Exception exception)
    {
        // Surface synchronously so a misconfigured benchmark fails loudly. Production
        // Blazor swallows + logs; for a benchmark we want the BDN runner to see the
        // stack and report a failed iteration.
        throw exception;
    }

    /// <summary>
    ///     Render the supplied component as a root and run a parameter-update cycle.
    ///     Returns the captured byte count from the most recent batch.
    /// </summary>
    public long RenderAsRootAndMeasure<TComponent>(ParameterView parameters)
        where TComponent : IComponent
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            var componentId = AssignRootComponentId(InstantiateComponent(typeof(TComponent)));
            await RenderRootComponentAsync(componentId, parameters);
            return LastBatchByteCount;
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Re-render an already-attached root with new parameters. Use this after the
    ///     first render so the captured batch is the *diff* between renders rather than
    ///     the initial attach (which contains the full tree as inserts).
    /// </summary>
    public long ReRenderRootAndMeasure(int componentId, ParameterView parameters)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            await RenderRootComponentAsync(componentId, parameters);
            return LastBatchByteCount;
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Two-shot: attach root with the first parameters and discard the initial-attach
    ///     batch, then re-render with the second parameters and return that batch's bytes.
    ///     This is the apples-to-apples shape against
    ///     <see cref="RaskHarness.SeedPrevious" /> + <see cref="RaskHarness.RenderAndBuildDiffPayloadBytes" />:
    ///     the returned bytes describe ONE incremental update, not the initial mount.
    /// </summary>
    public long MeasureIncrementalUpdate<TComponent>(ParameterView before, ParameterView after)
        where TComponent : IComponent
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            var componentId = AssignRootComponentId(InstantiateComponent(typeof(TComponent)));
            await RenderRootComponentAsync(componentId, before);
            // Discard the attach batch — caller wants the incremental cost.
            await RenderRootComponentAsync(componentId, after);
            return LastBatchByteCount;
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Sustained-load counterpart to <see cref="MeasureIncrementalUpdate{TComponent}" />.
    ///     Attaches the root ONCE, then drives <paramref name="cycles" /> re-renders each
    ///     using parameters from <paramref name="parametersFor" />. Returns the SUM of all
    ///     per-cycle batch byte counts. Used by <c>MemoryGc_*</c> benches so root
    ///     attachment isn't paid per cycle (Rask's stateful root also pays it only once).
    /// </summary>
    public long MeasureSustainedIncrementalUpdates<TComponent>(int cycles, Func<int, ParameterView> parametersFor)
        where TComponent : IComponent
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            var componentId = AssignRootComponentId(InstantiateComponent(typeof(TComponent)));
            // Discard initial attach. The caller-supplied parameters[0] is the seed shape;
            // measured cycles start from the i=1 re-render onward.
            await RenderRootComponentAsync(componentId, parametersFor(0));
            long total = 0;
            for (var i = 1; i <= cycles; i++)
            {
                await RenderRootComponentAsync(componentId, parametersFor(i));
                total += LastBatchByteCount;
            }

            return total;
        }).GetAwaiter().GetResult();
    }
}
