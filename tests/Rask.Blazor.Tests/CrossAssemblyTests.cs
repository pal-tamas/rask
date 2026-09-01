using Microsoft.Extensions.DependencyInjection;
using Rask.Blazor.Library.Fixture;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     The headline path: a component from a REFERENCED assembly, compiled from a real
///     <c>.razor</c> by the Razor SDK.
/// </summary>
/// <remarks>
///     Every other fixture here is a hand-written <c>ComponentBase</c> in this compilation, where the
///     symbols come from source. That cannot show what the feature actually promises — reading a
///     hosted component's <c>[Parameter]</c>s out of another assembly's metadata, which is what
///     hosting MudBlazor or your own Razor Class Library really does.
/// </remarks>
public partial class CrossAssemblyTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void An_empty_island_over_a_REFERENCED_razor_component_gets_its_steps()
    {
        var html = RaskTest.Render(TickerIsland.Symbol("RASK").Price(12.5m), Services()).Html;

        Assert.Contains("<strong>RASK</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<span>12.50</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorRequired_in_the_referenced_component_is_required_here()
    {
        // Ticker.razor marks Symbol [EditorRequired]; Tone and Note are plain [Parameter].
        var html = RaskTest.Render(TickerIsland.Symbol("RASK").Tone("up"), Services()).Html;

        Assert.Contains("class=\"ticker up\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_optional_parameter_leaves_the_components_own_markup_alone()
    {
        // Note is null, so its key is omitted and the component's @if never renders the <em>.
        var html = RaskTest.Render(TickerIsland.Symbol("RASK"), Services()).Html;

        Assert.DoesNotContain("<em>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_input_is_wired_to_Rasks_VALUE_channel_not_its_DOM_event_channel()
    {
        // @bind lowers to `value=` plus an ONCHANGE handler reading ChangeEventArgs.Value, so the
        // browser's value has to reach the hosted component. Rask carries a value on its input
        // channel, and `change` and `input` each dispatch their own frame carrying it.
        var html = RaskTest.Render(EditorIsland.Text("hello"), Services()).Html;

        Assert.Contains("data-rask-on-change=", html, StringComparison.Ordinal);

        // ONE attribute, never both. They carry separate ids and each dispatches its own frame, so
        // writing the same id to both fires the hosted handler twice per edit.
        Assert.DoesNotContain("data-rask-on-input=", html, StringComparison.Ordinal);

        // And the current value is rendered, so the input is not blank on first paint.
        Assert.Contains("value=\"hello\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typing_into_a_bound_input_reaches_the_hosted_component_and_re_renders()
    {
        // The round trip, not just the wiring: the browser's value travels Rask's value channel,
        // becomes a ChangeEventArgs, is dispatched into Blazor, assigns the bound field through the
        // binder @bind generated, and the component's own re-render reaches the page.
        var page = RaskTest.Render(EditorIsland.Text("hello"), Services());
        Assert.Contains("echo: hello", page.Html, StringComparison.Ordinal);

        await page.ChangeAsync("{\"value\":\"typed\"}");

        Assert.Contains("echo: typed", page.Html, StringComparison.Ordinal);
        Assert.Contains("value=\"typed\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clicking_the_hosted_components_own_element_reaches_its_EventCallback()
    {
        var picked = "";
        var page = RaskTest.Render(
            TickerIsland.Symbol("RASK").OnPick(s => picked = s),
            Services());

        await page.On("[data-rask-on-click]").ClickAsync();

        Assert.Equal("RASK", picked);
    }

    [Fact]
    public void A_hosted_components_text_and_attributes_are_HTML_ENCODED()
    {
        // The island's markup reaches the page through Raw, which is Rask's only un-encoded sink — so
        // whatever encodes it is this writer, and nothing downstream will catch a miss. A parameter
        // value is the obvious way for user input to arrive here.
        const string Attack = "<script>alert(1)</script>";

        var page = RaskTest.Render(EditorIsland.Text(Attack), Services());

        // Text content: the component echoes Text into a <p>.
        Assert.Contains("&lt;script&gt;", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", page.Html, StringComparison.Ordinal);

        // Attribute value: the same string also rides the input's value=, where an unescaped quote
        // would break out of the attribute even if the angle brackets were handled. The property to
        // assert is that the QUOTE is encoded — the words after it are inert inside a closed value,
        // so searching for "onfocus=" would fail on correct output.
        var quoted = RaskTest.Render(EditorIsland.Text("\" onfocus=\"alert(1)"), Services());

        Assert.Contains("value=\"&quot; onfocus=&quot;", quoted.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"\" onfocus", quoted.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_NESTED_island_generates_inside_its_container()
    {
        // The generated part used to be written at namespace scope, producing a second unrelated
        // top-level class — CS0101 plus a missing-abstract-member error, neither pointing at the
        // cause. That this compiles at all is most of the assertion.
        var html = RaskTest.Render(NestedTicker.Symbol("RASK"), Services()).Html;

        Assert.Contains("<strong>RASK</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_javascript_URL_in_a_hosted_attribute_is_neutralised()
    {
        // Encoding is not enough for a URL: javascript: survives HTML-encoding intact and runs on
        // click. Rask neutralises the scheme framework-wide, and the island's markup reaches the page
        // through Raw — its only un-encoded path — so this writer is the last place that can.
        var html = RaskTest
            .Render(TickerIsland.Symbol("RASK").Url("javascript:alert(1)"), Services())
            .Html;

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);

        // An ordinary URL is untouched, so the sanitizer is not just blanking every href.
        var ok = RaskTest.Render(TickerIsland.Symbol("RASK").Url("/quotes/rask"), Services()).Html;
        Assert.Contains("href=\"/quotes/rask\"", ok, StringComparison.Ordinal);
    }

    [Fact]
    public void A_renamed_parameter_actually_REACHES_the_hosted_component()
    {
        // The step existing is not the claim — the value arriving is. A hand-declared property used to
        // be skipped entirely when building the parameter writer, so [BlazorParameter] produced a
        // chain step that accepted a value and silently never passed it on.
        var html = RaskTest.Render(RenamedIsland.Symbol("RASK").Annotation("halted"), Services()).Html;

        Assert.Contains("<em>halted</em>", html, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void Rask_children_render_inside_the_referenced_component()
    {
        var html = RaskTest
            .Render(TickerIsland.Symbol("RASK")[Button.OnClick(() => { })["Buy"]], Services())
            .Html;

        Assert.Contains("Buy", html, StringComparison.Ordinal);

        // TWO click handlers, and the count is the whole point: Ticker wires its own @onclick, so
        // asserting that "data-rask-on-click" appears at all would pass with the child's handler
        // dropped entirely. The child's is Rask's own, delegated from document, and survives the
        // island — that is the claim.
        Assert.Equal(2, Occurrences(html, "data-rask-on-click="));
    }
}

/// <summary>An island over a component this assembly does not declare. Body deliberately empty.</summary>
public sealed partial class TickerIsland : BlazorComponent<Ticker>;

/// <summary>An island over a component that uses real two-way <c>@bind</c>.</summary>
public sealed partial class EditorIsland : BlazorComponent<Editor>;

/// <summary>A NESTED island — its generated part has to land inside its container, not beside it.</summary>
public static partial class Widgets
{
    /// <summary>Nested one level deep, which used to emit a second unrelated top-level class.</summary>
    public sealed partial class NestedTicker : BlazorComponent<Ticker>;
}

/// <summary>An island that renames one of the hosted component's parameters for the call site.</summary>
public sealed partial class RenamedIsland : BlazorComponent<Ticker>
{
    /// <summary>Feeds <c>Ticker.Note</c> under a name that reads better in a chain.</summary>
    [BlazorParameter("Note")]
    public string? Annotation { get; set; }
}
