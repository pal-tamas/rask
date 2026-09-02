using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     PROBE (not a contract yet): what a hosted Blazor component can actually reach of Rask's
///     browser-API surface, and from where.
/// </summary>
/// <remarks>
///     <c>docs/blazor-components.md</c> lists <c>IJSRuntime</c> as unavailable, and
///     <c>AddRaskBlazor</c> does register a runtime that throws. But it registers it with
///     <c>TryAdd</c>, and both hosts register their own <c>IJSRuntime</c> first — so in a real app
///     the throwing shim never wins. These tests establish which half of the claim is true.
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

public sealed partial class ClipboardIsland : BlazorComponent<ClipboardBox>;

public sealed partial class ThemeIsland : BlazorComponent<ThemeBox>;
