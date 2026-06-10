using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.TestSupport;

/// <summary>
///     Collapses the repeated live-render setup found across lifecycle tests —
///     <c>new ServiceCollection().BuildServiceProvider()</c> followed by
///     <see cref="LiveRenderContext.Begin(Component, IServiceProvider?)" /> →
///     <c>GetOrCreate</c> → <c>NotifyParameters</c>.
/// </summary>
public static class RenderHarness
{
    /// <summary>An empty <see cref="IServiceProvider" /> for tests that need no registrations.</summary>
    public static IServiceProvider EmptyServices() => new ServiceCollection().BuildServiceProvider();

    /// <summary>
    ///     Begins a <see cref="LiveRenderContext" /> for <paramref name="component" />, resolves
    ///     it, and fires <c>NotifyParameters</c>. Dispose the returned scope to end the context.
    /// </summary>
    public static RenderScope<T> Render<T>(T component, IServiceProvider services, bool propsChanged = true)
        where T : Component
    {
        var ctx = LiveRenderContext.Begin(component, services);
        var resolved = ctx.GetOrCreate(_ => component);
        ctx.NotifyParameters(resolved, propsChanged);
        return new RenderScope<T>(ctx, resolved);
    }

    public readonly struct RenderScope<T> : IDisposable
        where T : Component
    {
        internal RenderScope(LiveRenderContext ctx, T resolved)
        {
            Context = ctx;
            Resolved = resolved;
        }

        public LiveRenderContext Context { get; }

        public T Resolved { get; }

        public void Dispose() => Context.Dispose();
    }
}
