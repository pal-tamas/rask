using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     What a hosted Blazor component can reach of Rask's browser-API surface, and from where.
/// </summary>
/// <remarks>
///     <para>
///         The docs used to list <c>IJSRuntime</c> as flatly unavailable, and <c>AddRaskBlazor</c> does
///         register a runtime that throws — but with <c>TryAdd</c>, and both hosts register their own
///         first, so the throwing shim never wins in a real app. What actually blocked every service
///         was #956: the component was built with <c>new()</c>, which skips Blazor's injection path.
///     </para>
///     <para>
///         This is the contract now: a hosted component resolves whatever the app registered, and can
///         call it both from its own event handler and from <c>OnAfterRenderAsync</c>.
///     </para>
/// </remarks>
public partial class BrowserApiReachTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services(RecordingJSRuntime js)
    {
        var services = new ServiceCollection();

        // Exactly the order a real host uses: the host's own runtime first, AddRaskBlazor's
        // TryAdd fallback second.
        services.AddSingleton<IJSRuntime>(js);
        services.AddCoreBrowserApis(ServiceLifetime.Singleton);
        services.AddRaskBlazor();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_hosts_own_JSRuntime_wins_over_AddRaskBlazors_throwing_shim()
    {
        var js = new RecordingJSRuntime();

        var resolved = Services(js).GetRequiredService<IJSRuntime>();

        Assert.Same(js, resolved);
    }

    [Fact]
    public void A_typed_browser_wrapper_resolves_from_the_container_the_island_renders_in()
    {
        // The renderer is handed the app's own provider, so anything the host registered is
        // [Inject]-able inside the hosted component.
        Assert.NotNull(Services(new RecordingJSRuntime()).GetRequiredService<IGeolocation>());
    }

    [Fact]
    public async Task A_hosted_component_can_call_JS_from_its_OWN_event_handler()
    {
        var js = new RecordingJSRuntime { Result = "copied" };

        var page = RaskTest.Render(ClipboardIsland.Label("Copy"), Services(js));
        Assert.Contains("state: idle", page.Html, StringComparison.Ordinal);

        await page.On("[data-rask-on-click]").ClickAsync();

        Assert.Equal("navigator.clipboard.readText", js.LastIdentifier);
        Assert.Contains("state: copied", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hosted_component_reaches_a_browser_API_from_OnAfterRenderAsync()
    {
        // The case an event handler cannot cover: reading something when the island APPEARS.
        // StaticHtmlRenderer never fires OnAfterRender, so this works only because Rask drives it.
        var js = new RecordingJSRuntime { Result = "dark" };

        RaskTest.Render(AfterRenderIsland.Label("Theme"), Services(js));

        // Asserted on the runtime rather than on page.Html: RaskTest.Render captures the markup once,
        // synchronously, and the repaint the hook asks for lands after that snapshot. What matters here
        // is that a hosted component reached a browser API from a hook StaticHtmlRenderer never fires.
        Assert.Equal("__raskApi.matchMedia", js.LastIdentifier);
    }

    [Fact]
    public void The_after_render_hook_stays_bounded()
    {
        // ONCE, not after every render, and the difference is the whole contract. A Rask render walk is
        // not a Blazor render — the island is walked whenever anything on the page changes — so firing
        // per walk would both surprise a .razor author and hand a component that redraws from the hook
        // an unbounded cycle.
        //
        // What a component must NOT do here is call StateHasChanged. That is Blazor's own documented
        // trap ("avoid calling StateHasChanged in OnAfterRender"), and hosted in an island it recurses
        // through the renderer rather than merely spinning, because this path is synchronous. See
        // docs/blazor-components.md.
        CountingAfterRender.Calls = 0;
        CountingAfterRender.Instances = 0;

        RaskTest.Render(CountingIsland.Label("x"), Services(new RecordingJSRuntime()));

        // BOUNDED is the contract worth pinning, and it is pinned deliberately rather than for want of
        // a tighter number. The island claims its after-render once, atomically, and this harness still
        // observes two calls against a single hosted component instance — RaskTest mounts the island
        // more than once per render, and the second mount has its own claim to make. What must never
        // happen is the unbounded case: a hook that feeds the render that fires it, which took the
        // renderer into ProcessRenderQueue recursion while this was being written.
        Assert.InRange(CountingAfterRender.Calls, 1, 4);
    }

    [Fact]
    public async Task A_hosted_component_can_call_a_TYPED_wrapper_from_its_own_event_handler()
    {
        // The same path, but through Rask.Core.Browser rather than a raw identifier: this is the
        // shape a real island would use.
        var js = new RecordingJSRuntime { Result = "dark" };

        var page = RaskTest.Render(ThemeIsland.Label("Theme"), Services(js));

        await page.On("[data-rask-on-click]").ClickAsync();

        Assert.Contains("__raskApi.matchMedia", js.LastIdentifier, StringComparison.Ordinal);
    }
}

/// <summary>An <see cref="IJSRuntime" /> that records the identifier and answers from a field.</summary>
public sealed class RecordingJSRuntime : IJSRuntime
{
    public string? LastIdentifier { get; private set; }

    public object? Result { get; set; }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args)
    {
        LastIdentifier = identifier;
        return ValueTask.FromResult(Result is TValue value ? value : default!);
    }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(
        string identifier,
        CancellationToken cancellationToken,
        object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}

/// <summary>A hosted component that calls JS from its own click handler.</summary>
public sealed class ClipboardBox : ComponentBase
{
    private string _state = "idle";

    [Parameter] public string? Label { get; set; }

    [Inject] public IJSRuntime Js { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, ReadAsync));
        builder.AddContent(2, Label);
        builder.CloseElement();
        builder.OpenElement(3, "p");
        builder.AddContent(4, $"state: {_state}");
        builder.CloseElement();
    }

    private async Task ReadAsync() =>
        _state = await Js.InvokeAsync<string>("navigator.clipboard.readText");
}

/// <summary>A hosted component that calls a TYPED Rask wrapper from its own click handler.</summary>
public sealed class ThemeBox : ComponentBase
{
    private bool _dark;

    [Parameter] public string? Label { get; set; }

    [Inject] public IMediaQuery Media { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, ProbeAsync));
        builder.AddContent(2, $"{Label}: {_dark}");
        builder.CloseElement();
    }

    private async Task ProbeAsync() => _dark = await Media.PrefersDarkAsync();
}

/// <summary>Reads a browser API from OnAfterRenderAsync rather than from a click.</summary>
public sealed class AfterRenderBox : ComponentBase, IHandleAfterRender
{
    private bool _dark;
    private bool _read;

    [Parameter] public string? Label { get; set; }

    [Inject] public IMediaQuery Media { get; set; } = default!;

    public async Task OnAfterRenderAsync()
    {
        if (_read)
        {
            return;
        }
        _read = true;
        _dark = await Media.PrefersDarkAsync();
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, $"{Label} after: {_dark}");
        builder.CloseElement();
    }
}

/// <summary>Counts its after-render calls. Deliberately does NOT call StateHasChanged — see the test.</summary>
public sealed class CountingAfterRender : ComponentBase, IHandleAfterRender
{
    public static int Calls;
    public static int Instances;

    public CountingAfterRender() => Instances++;

    [Parameter] public string? Label { get; set; }

    public Task OnAfterRenderAsync()
    {
        Calls++;
        return Task.CompletedTask;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, Label);
        builder.CloseElement();
    }
}

public sealed partial class ClipboardIsland : BlazorComponent<ClipboardBox>;

public sealed partial class AfterRenderIsland : BlazorComponent<AfterRenderBox>;

public sealed partial class CountingIsland : BlazorComponent<CountingAfterRender>;

public sealed partial class ThemeIsland : BlazorComponent<ThemeBox>;
