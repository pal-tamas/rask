using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Wasm.Tests.Session;

/// <summary>
///     The browser session enters the app's <see cref="ISessionWorkScope" /> around its work, as the server's does —
///     which is how a Rask.Data save in a click handler finds the page's query cache.
/// </summary>
[Collection("WasmSession")]
public class WorkScopeTests
{
    [Fact]
    public async Task The_first_render_and_a_click_handler_both_run_inside_the_work_scope()
    {
        var app = new ScopeRecordingApp();
        var (session, services) = NewSession(
            _ => app, s => s.AddSingleton<ISessionWorkScope, RecordingWorkScope>());

        var initial = await session.InitialRenderAsync();
        var renderedIn = app.RenderedIn;
        await session.DispatchAsync(
            Encoding.UTF8.GetBytes($$"""{"id":"{{MarkupAssert.FirstHandlerId(initial)}}","type":"click"}"""));

        Assert.Same(services, renderedIn);
        Assert.Same(services, app.ClickedIn);
    }

    [Fact]
    public async Task A_session_with_no_work_scope_registered_renders_and_dispatches_as_before()
    {
        var app = new ScopeRecordingApp();
        var (session, _) = NewSession(_ => app);

        var initial = await session.InitialRenderAsync();
        await session.DispatchAsync(
            Encoding.UTF8.GetBytes($$"""{"id":"{{MarkupAssert.FirstHandlerId(initial)}}","type":"click"}"""));

        Assert.Null(app.RenderedIn);
        Assert.True(app.Clicked);
    }
}

/// <summary>Makes the session's services ambient, the way Rask.Data's scope does, so the app can read them back.</summary>
internal sealed class RecordingWorkScope : ISessionWorkScope
{
    private static readonly AsyncLocal<IServiceProvider?> Ambient = new();

    public static IServiceProvider? Current => Ambient.Value;

    public IDisposable? Enter(IServiceProvider sessionServices)
    {
        var previous = Ambient.Value;
        Ambient.Value = sessionServices;
        return new Restore(previous);
    }

    private sealed class Restore(IServiceProvider? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

internal sealed partial class ScopeRecordingApp : Component
{
    public IServiceProvider? RenderedIn;
    public IServiceProvider? ClickedIn;
    public bool Clicked;

    protected override Component? Render()
    {
        RenderedIn ??= RecordingWorkScope.Current;
        return Button.OnClick(() =>
        {
            Clicked = true;
            ClickedIn = RecordingWorkScope.Current;
        })["save"];
    }
}
