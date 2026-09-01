using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Rask.Blazor.Tests;

/// <summary>
///     A hand-written Blazor component: exactly what the Razor SDK compiles a <c>.razor</c> into.
/// </summary>
/// <remarks>
///     Written by hand rather than compiled from a <c>.razor</c> so this project needs no Razor SDK.
///     The path under test is identical — <c>BuildRenderTree</c> is what the SDK emits.
/// </remarks>
public sealed class Greeting : ComponentBase
{
    [Parameter] public string? Heading { get; set; }
    [Parameter] public int Count { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    public int InitializedCalls;
    public int ParametersSetCalls;

    protected override void OnInitialized() => InitializedCalls++;
    protected override void OnParametersSet() => ParametersSetCalls++;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "class", "greeting");
        builder.AddContent(2, Heading ?? "(none)");
        builder.AddContent(3, "/");
        builder.AddContent(4, Count);
        if (ChildContent is not null)
        {
            builder.AddContent(5, ChildContent);
        }

        builder.CloseElement();
    }
}

/// <summary>A hosted component whose markup is only correct once an await has finished.</summary>
public sealed class SlowGreeting : ComponentBase
{
    [Parameter] public string? Heading { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await Task.Delay(20);
        Heading += " (loaded)";
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddContent(1, Heading);
        builder.CloseElement();
    }
}

/// <summary>The island under test. <c>WriteParameters</c> is hand-written until the generator lands.</summary>
public sealed partial class GreetingIsland : BlazorComponent<Greeting>
{
    public string? Heading { get; set; }
    public int? Count { get; set; }

    protected override void WriteParameters(Dictionary<string, object?> into)
    {
        // Nullable + null OMITS its key, so the hosted component keeps its own default.
        if (Heading is not null)
        {
            into["Heading"] = Heading;
        }

        if (Count is not null)
        {
            into["Count"] = Count.Value;
        }
    }
}

/// <summary>Stands in for MudTable: a hosted component with its own click handler.</summary>
public sealed class Clicker : ComponentBase
{
    [Parameter] public EventCallback<int> OnPick { get; set; }
    [Parameter] public string[] Rows { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ul");
        for (var i = 0; i < Rows.Length; i++)
        {
            var row = i;
            builder.OpenElement(1, "li");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => OnPick.InvokeAsync(row)));
            builder.AddContent(3, Rows[i]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}

/// <summary>An island over the clickable component.</summary>
public sealed partial class ClickerIsland : BlazorComponent<Clicker>
{
    public string[]? Rows { get; set; }
    public Action<int>? OnPick { get; set; }

    protected override void WriteParameters(Dictionary<string, object?> into)
    {
        if (Rows is not null)
        {
            into["Rows"] = Rows;
        }

        if (OnPick is not null)
        {
            into["OnPick"] = EventCallback.Factory.Create<int>(this, OnPick);
        }
    }
}

/// <summary>An island over the awaiting component, for the first-paint test.</summary>
public sealed partial class SlowIsland : BlazorComponent<SlowGreeting>
{
    public string? Heading { get; set; }

    protected override void WriteParameters(Dictionary<string, object?> into)
    {
        if (Heading is not null)
        {
            into["Heading"] = Heading;
        }
    }
}
