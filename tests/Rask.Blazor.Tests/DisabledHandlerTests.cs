using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Testing;

namespace Rask.Blazor.Tests;

// A hosted control drawn disabled takes no event, as a Rask one does not (#1274, after #1273): the browser sends none to
// a disabled button, input, select or textarea — nor to one inside a <fieldset disabled>, outside its first legend — so
// what arrives for one was written by hand. The handler never runs; the markup is an enabled control's.
//
// One collection, not parallel: the refusal is said through RaskDiagnostics.Sink, which is process-wide.
[Collection(nameof(DisabledHandlerCollection))]
public partial class DisabledHandlerTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task A_hosted_disabled_button_refuses_a_click_sent_by_hand()
    {
        var page = Page.Render(DisabledIsland, Services());

        var reported = await Reported(() => page.On("#own").Click());

        Assert.Contains("clicked: none", page.Html, StringComparison.Ordinal);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Contains("button 'own'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_button_disabled_by_a_false_string_still_refuses_a_click()
    {
        var page = Page.Render(DisabledIsland, Services());

        await page.On("#stringly").Click();

        Assert.Contains("clicked: none", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_button_inside_a_disabled_fieldset_refuses_a_click_sent_by_hand()
    {
        var page = Page.Render(DisabledIsland, Services());

        await page.On("#held").Click();

        Assert.Contains("clicked: none", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_input_inside_a_disabled_fieldset_refuses_a_value_sent_by_hand()
    {
        var page = Page.Render(DisabledIsland, Services());

        await page.On("#typed").Change("written");

        Assert.Contains("typed: none", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_button_in_a_disabled_fieldsets_first_legend_still_takes_a_click()
    {
        var page = Page.Render(DisabledIsland, Services());

        await page.On("#legend").Click();

        Assert.Contains("clicked: legend", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_enabled_hosted_button_still_takes_a_click()
    {
        var page = Page.Render(DisabledIsland, Services());

        var reported = await Reported(() => page.On("#free").Click());

        Assert.Contains("clicked: free", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_hosted_button_whose_disabled_is_false_still_takes_a_click()
    {
        var page = Page.Render(DisabledIsland, Services());

        await page.On("#off").Click();

        Assert.Contains("clicked: off", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_button_inside_a_disabled_Rask_fieldset_refuses_a_click_sent_by_hand()
    {
        var page = Page.Render(() => Fieldset.Disabled(true)[DisabledIsland], Services());

        await page.On("#free").Click();

        Assert.Contains("clicked: none", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hosted_disabled_button_is_drawn_with_its_handler_as_an_enabled_one_is()
    {
        var page = Page.Render(DisabledIsland, Services());

        var html = page.Html;

        Assert.Matches("<button id=\"own\" name=\"own\" disabled data-rask-on-click=\"[^\"]+\"", html);
    }

    private static async Task<List<RaskDiagnosticEvent>> Reported(Func<Task> act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = e =>
        {
            lock (captured)
            {
                captured.Add(e);
            }
        };
        try
        {
            await act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }
}

[CollectionDefinition(nameof(DisabledHandlerCollection), DisableParallelization = true)]
public sealed class DisabledHandlerCollection;

/// <summary>What <c>&lt;button disabled @onclick&gt;</c> and a <c>&lt;fieldset disabled&gt;</c> compile to, beside enabled ones.</summary>
public sealed class DisabledBox : ComponentBase
{
    private string _clicked = "none";
    private string _typed = "none";

    // Each in its own region, as a Razor helper's markup is: the regions are the fieldset's children too.
    private void Button(RenderTreeBuilder builder, string id, object? disabled)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "id", id);
        builder.AddAttribute(2, "name", id);
        builder.AddAttribute(3, "disabled", disabled);
        builder.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, () => _clicked = id));
        builder.CloseElement();
    }

    private void Region(RenderTreeBuilder builder, int sequence, string id, object? disabled)
    {
#pragma warning disable ASP0006 // one region per button, numbered by the caller's literal
        builder.OpenRegion(sequence);
#pragma warning restore ASP0006
        Button(builder, id, disabled);
        builder.CloseRegion();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Region(builder, 0, "own", true);
        Region(builder, 10, "stringly", "false");
        Region(builder, 20, "off", false);
        Region(builder, 30, "free", null);

        builder.OpenElement(40, "fieldset");
        builder.AddAttribute(41, "disabled", true);
        builder.OpenElement(42, "legend");
        Region(builder, 50, "legend", null);
        builder.CloseElement();
        builder.OpenElement(60, "div");
        Region(builder, 70, "held", null);
        builder.OpenElement(80, "input");
        builder.AddAttribute(81, "id", "typed");
        builder.AddAttribute(82, "onchange", EventCallback.Factory.CreateBinder(this, v => _typed = v ?? "none", _typed));
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(90, "p");
        builder.AddContent(91, $"clicked: {_clicked} typed: {_typed}");
        builder.CloseElement();
    }
}

public sealed partial class DisabledIsland : BlazorComponent<DisabledBox>;
