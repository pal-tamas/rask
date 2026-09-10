using Microsoft.AspNetCore.Components;

namespace Rask.Benchmarks.VsBlazor.Infrastructure;

/// <summary>
///     Dispatcher that runs every work item synchronously on the calling thread.
///     <para>
///         Replaces <see cref="Dispatcher.CreateDefault" /> inside benchmarks. The default
///         dispatcher posts continuations through a <c>RendererSynchronizationContext</c>
///         queue. Under BDN's default job — many tight iterations on a single thread —
///         the queued continuations from <c>Renderer.RenderRootComponentAsync</c> stack
///         up and the wait inside <c>InvokeAsync(...).GetAwaiter().GetResult()</c>
///         deadlocks. Forcing <see cref="CheckAccess" /> to <c>true</c> makes every
///         internal <c>Dispatcher.InvokeAsync</c> Renderer call run inline, so the whole
///         render completes synchronously on the BDN iteration thread with no queuing.
///     </para>
///     <para>
///         Safe because benchmarks are single-threaded by construction; no concurrent
///         dispatch happens against a single <see cref="BlazorRenderBatchCapture" />.
///     </para>
/// </summary>
public sealed class InlineDispatcher : Dispatcher
{
    public override bool CheckAccess() => true;

    public override Task InvokeAsync(Action workItem)
    {
        workItem();
        return Task.CompletedTask;
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) => Task.FromResult(workItem());

    public override Task InvokeAsync(Func<Task> workItem) => workItem();

    public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem) => workItem();
}
