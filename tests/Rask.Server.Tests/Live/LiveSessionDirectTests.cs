using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Globalization;
using Rask.Core.Live;

namespace Rask.Server.Tests.Live;

public class LiveSessionDirectTests
{
    [Fact]
    public async Task A_render_request_with_no_socket_does_nothing()
    {
        using var session = NewSession(new BasicComponent());

        await session.RequestRenderAsync();
    }

    [Fact]
    public void Disposing_the_session_disposes_its_scope_and_its_component_tree()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var disposable = new TrackingDisposable();
        var session = new LiveSession("id", disposable, scope, LiveDiffMode.Auto);

        session.Dispose();

        Assert.Equal(1, disposable.Disposes);
    }

    // #1093: both disposal paths unhook the culture subscription, and neither delegates to the other, so each is
    // pinned. The culture service is scoped per session on this host, but the contract is the same as WASM's.
    [Fact]
    public void Disposing_the_session_unsubscribes_from_CultureChanged()
    {
        var (session, culture) = NewCultureSession();

        session.Dispose();

        Assert.Equal(0, culture.SubscriberCount);
    }

    [Fact]
    public async Task Disposing_the_session_asynchronously_unsubscribes_from_CultureChanged()
    {
        var (session, culture) = NewCultureSession();

        await session.DisposeAsync();

        Assert.Equal(0, culture.SubscriberCount);
    }

    private static (LiveSession Session, CountingCulture Culture) NewCultureSession()
    {
        // Process-wide and not reset: it only decides whether a session looks for a culture service.
        RaskCulture.IsEnabled = true;
        var culture = new CountingCulture();
        var sp = new ServiceCollection().AddSingleton<IRaskCulture>(culture).BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var session = new LiveSession("id", new BasicComponent(), scope, LiveDiffMode.Auto);
        Assert.Equal(1, culture.SubscriberCount);
        return (session, culture);
    }

    [Fact]
    public async Task Disposing_the_session_asynchronously_runs_the_trees_async_dispose()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var disposable = new TrackingAsyncDisposable();
        var session = new LiveSession("id", disposable, scope, LiveDiffMode.Auto);

        await session.DisposeAsync();

        Assert.Equal(1, disposable.Disposes);
    }

    [Fact]
    public async Task The_constructor_assigns_the_render_handle_so_StateHasChangedAsync_routes_to_the_session()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var view = new BasicComponent();

        using var session = new LiveSession("id", view, scope, LiveDiffMode.Auto);

        await view.StateHasChangedAsync();
    }

    private static LiveSession NewSession(Component view)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return new LiveSession(Guid.NewGuid().ToString("N"), view, scope, LiveDiffMode.Auto);
    }

    private sealed class BasicComponent : Component
    {
        protected override Component? Render() => new Span();
    }

    private sealed class TrackingDisposable : Component, IDisposable
    {
        public int Disposes;
        public void Dispose() => Disposes++;
        protected override Component? Render() => new Span();
    }

    private sealed class TrackingAsyncDisposable : Component, IAsyncDisposable
    {
        public int Disposes;

        public ValueTask DisposeAsync()
        {
            Disposes++;
            return ValueTask.CompletedTask;
        }

        protected override Component? Render() => new Span();
    }
}
