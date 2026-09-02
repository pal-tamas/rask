using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     The whole seam: a real Blazor component, mounted by Rask, rendered into the page.
/// </summary>
public partial class BlazorRenderTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Island_renders_the_hosted_components_markup_inside_the_host_element()
    {
        var html = RaskTest.Render(GreetingIsland.Heading("Hi").Count(3), Services()).Html;

        Assert.Contains("<rask-blazor", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"greeting\">Hi/3</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_element_names_the_island_and_the_hosted_type()
    {
        var html = RaskTest.Render(GreetingIsland.Heading("Hi"), Services()).Html;

        Assert.Contains($"{BlazorDefaults.NameAttribute}=\"GreetingIsland\"", html, StringComparison.Ordinal);
        Assert.Contains(
            $"{BlazorDefaults.ComponentAttribute}=\"{typeof(Greeting).FullName}\"",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_statically_rendered_island_is_not_opaque()
    {
        // Load-bearing, not cosmetic: FrameDiffer SKIPS an opaque element's children, so an opaque
        // static island would render once on the server and never ship a change again.
        var html = RaskTest.Render(GreetingIsland.Heading("Hi"), Services()).Html;

        Assert.DoesNotContain("data-rask-opaque", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_awaiting_hosted_component_is_complete_in_the_FIRST_paint()
    {
        // The entire value claim of static hosting, and it has to be asserted on the QUIESCENT path
        // rather than through RaskTest.Render: that helper renders once, synchronously, so it can
        // neither prove nor disprove this. The server renders in waves and sends the settled one.
        QuiescenceScope.ResetSyncForTests();

        var island = SlowIsland.Heading("Hello").Value;
        var services = Services();

        var result = await QuiescentRender.RunAsync(
            _ => RaskTest.Render(island, services).Html,
            TimeSpan.FromSeconds(5));

        Assert.Contains("Hello (loaded)", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_island_with_an_EMPTY_body_takes_its_chain_steps_from_the_hosted_component()
    {
        // Nothing is declared on EmptyIsland. Heading and Count are Greeting's own [Parameter]s, and
        // both the property and its chain setter are generated from that one source of truth.
        var html = RaskTest.Render(EmptyIsland.Heading("Hi").Count(9), Services()).Html;

        Assert.Contains("<p class=\"greeting\">Hi/9</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_islands_generated_steps_are_OPTIONAL_not_required()
    {
        // A non-nullable property with no initializer would be a REQUIRED step (RASK001), which would
        // force every call site to supply every parameter the hosted component happens to declare.
        var html = RaskTest.Render(EmptyIsland.Count(4), Services()).Html;

        Assert.Contains("(none)/4", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_EditorRequired_parameter_becomes_a_REQUIRED_chain_step()
    {
        // Blazor already has a word for "mandatory", so it maps onto Rask's own rather than every
        // hosted parameter being optional. Label opens the chain because it is required; Tone, which
        // is not marked, stays an ordinary optional step.
        var html = RaskTest.Render(BadgeIsland.Label("New").Tone("warn"), Services()).Html;

        Assert.Contains("<span class=\"warn\">New</span>", html, StringComparison.Ordinal);

        // And the optional one really is optional — the hosted component keeps its own default.
        var bare = RaskTest.Render(BadgeIsland.Label("New"), Services()).Html;
        Assert.Contains("<span class=\"plain\">New</span>", bare, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosted_components_OWN_event_handlers_are_wired_to_Rasks_channel()
    {
        // Blazor assigns a handler id to every @onclick even in a static render; its own HTML writer
        // just drops them. We write Rask's attribute instead, so the click travels the socket that is
        // already open — no circuit, no blazor.web.js, no second connection.
        var html = RaskTest.Render(ClickerIsland.Rows(["alpha", "beta"]), Services()).Html;

        Assert.Contains("data-rask-on-click=", html, StringComparison.Ordinal);
        // One per row: the handler ids are per element, not per component.
        Assert.Equal(2, Occurrences(html, "data-rask-on-click="));
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
    public void A_null_nullable_prop_omits_its_key_so_the_component_keeps_its_own_value()
    {
        // The inversion from the islands feature: ParameterView is authoritative, so writing null
        // would CLOBBER the hosted component's default rather than mean "unset".
        var html = RaskTest.Render(GreetingIsland.Count(7), Services()).Html;

        Assert.Contains("(none)/7", html, StringComparison.Ordinal);
    }
}
