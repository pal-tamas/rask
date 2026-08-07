using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Diagnostics;

/// <summary>
///     The framework's throw messages have to name the remedy, not just the cause (#611). Most of
///     <c>Rask.Core</c> already did — <c>Navigator</c>, <c>Context</c>, <c>RouteAuthorizationGuard</c>,
///     <c>ExpressionAccessor</c>, <c>RaskJSRuntime</c> are the models. These are the ones that didn't.
/// </summary>
/// <remarks>
///     Asserting on message text is usually a smell, and here it is the point: the text IS the feature.
///     Each case asserts the two things that were missing — enough context to find the offending thing,
///     and a concrete instruction — rather than the exact sentence, so rewording stays free.
/// </remarks>
public class ActionableExceptionMessageTests
{
    // #611 calls this "the worst one", and it turned out to be unreachable: both callers of
    // ResolveContext already gate on `Model is not null || Context is not null`, which is exactly the
    // condition the throw checks. A Form with neither renders as a plain <form>, deliberately. So these
    // drive ResolveContext directly — the guard is kept for the next caller, and the message is worth
    // fixing for when that happens, but nobody has ever seen the old one.
    [Fact]
    public void A_form_with_neither_a_model_nor_a_context_renders_as_a_plain_form()
    {
        var view = new StubComponent(() => Form(Id: "plain")[Div()]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("<form id=\"plain\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_form_binding_guard_names_the_form_and_shows_both_ways_to_give_it_one()
    {
        var form = new Form { Id = "signup" };

        var ex = Assert.Throws<InvalidOperationException>(() => form.ResolveContext());

        Assert.Contains("#signup", ex.Message, StringComparison.Ordinal);       // which form
        Assert.Contains("Form(model)", ex.Message, StringComparison.Ordinal);   // the shape
        Assert.Contains("EditContext", ex.Message, StringComparison.Ordinal);   // the other way
        // "Context" alone was ambiguous between the Context<T> component and an EditContext.
        Assert.DoesNotContain("Form requires Model or Context.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_form_binding_guard_invents_no_label_when_there_is_nothing_to_name()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new Form().ResolveContext());

        Assert.Contains("Form has neither", ex.Message, StringComparison.Ordinal);
    }

    // The runtime siblings of RASK003. Each used to echo the offending segment, show no correct one, and
    // carry nothing to say which route it came from.
    [Theory]
    [InlineData("/docs/{}", "{id}")]
    [InlineData("/files/{**}", "{**path}")]
    [InlineData("/files/{*}", "{*path}")]
    [InlineData("/items/{:guid}", "{id:guid?}")]
    public void A_malformed_route_names_the_template_and_shows_a_correct_segment(
        string template, string suggestion)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => RoutePattern.Parse(template));

        Assert.Contains(template, ex.Message, StringComparison.Ordinal);
        Assert.Contains(suggestion, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outlet_outside_a_router_uses_the_same_words_as_the_router_itself()
    {
        // Two spellings for one condition meant searching the message found half the story, and which of
        // them you hit depended only on whether there was a live context or merely no route in it.
        var outlet = ReadSource("src", "Rask.Core", "Routing", "Outlet.cs");
        var renderer = ReadSource("src", "Rask.Core", "Routing", "RouteChainRenderer.cs");

        const string Shared = "Place Outlet() inside a Router(...) render tree.";
        Assert.Contains(Shared, outlet, StringComparison.Ordinal);
        Assert.Contains(Shared, renderer, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Outlet() must be called inside a Router render tree.", outlet, StringComparison.Ordinal);
    }

    [Fact]
    public void DragDrop_without_a_body_shows_the_delegate_shape()
    {
        var view = new StubComponent(() => DragDrop());

        var ex = Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());

        Assert.Contains("DragDrop(Body:", ex.Message, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine([LocateRepoRoot(), .. parts]));

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
