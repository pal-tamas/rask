using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class FormTests : global::Rask.Core.RaskMarkup
{
    // These assert what a <form> RENDERS, and a model renders nothing — so binding one leaves every
    // expectation below untouched. `Form` requires it because a form with nothing to bind to has no
    // fields that can resolve, which is a compile error now rather than a throw at first render.
    private static readonly Person Empty = new();

    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<form></form>", Form.Model(Empty).ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<form id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" enctype=\"multipart/form-data\" target=\"_blank\" accept-charset=\"utf-8\" autocomplete=\"off\" novalidate name=\"n\"></form>",
            Form.Model(Empty)
                .Enctype("multipart/form-data").Target("_blank").AcceptCharset("utf-8")
                .Autocomplete("off").Novalidate(true).Name("n")
                .Id("i").Class("c").Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<form>&lt;x&gt;</form>", Form.Model(Empty)["<x>"].ToHtml());

    [Fact]
    public void OnAnySubmit_outside_a_live_context_emits_no_handler_attribute() =>
        Assert.Equal(
            "<form></form>",
            Form.Model(Empty).OnAnySubmit(_ => { }).ToHtml());

    [Fact]
    public void OnAnySubmit_inside_a_live_context_emits_the_submit_handler_id()
    {
        var view = new StubComponent(() => Form.Model(Empty).OnAnySubmit(_ => { }));

        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void An_async_OnAnySubmit_inside_a_live_context_emits_the_submit_handler_id()
    {
        var view = new StubComponent(() => Form.Model(Empty).OnAnySubmit(async _ => { await Task.Yield(); }));

        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public async Task The_submit_bridge_awaits_async_validation_before_routing()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var validCalled = 0;
        var invalidCalled = 0;
        var ctx = new EditContext(p);
        ctx.AddValidator(new RejectingAsyncValidator());

        var view = new StubComponent(() => Form.Model(p)
            .OnSubmit(_ => validCalled++)
            .OnInvalidSubmit(_ => invalidCalled++)
            .Context(ctx)[Input.Bind(() => p.Name), Input.Bind(() => p.Age)]);
        var html = view.RenderAsLiveRoot();

        var submitId = MarkupAssert.Attr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");
        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);

        Assert.Equal(0, validCalled);
        Assert.Equal(1, invalidCalled);
    }

    [Fact]
    public void Submit_state_children_are_built_as_not_submitting() =>
        Assert.Equal(
            "<form>idle</form>",
            Form.Model(Empty)[f => [f.Submitting ? "busy" : "idle"]].ToHtml());

    // The old syntax is the point of the overload, not a side effect of it: a fixed list still binds to
    // the typed indexer, and a bare string still reaches the loose one as ONE text child rather than one
    // child per character. A lambda whose parameter is untyped and whose body is a collection expression
    // has no natural type, which is what keeps it out of the `params object?[]` overload's way.
    [Fact]
    public void Fixed_children_still_bind_to_the_list_indexers()
    {
        Assert.Equal("<form><span></span></form>", Form.Model(Empty)[Span].ToHtml());
        Assert.Equal("<form>&lt;x&gt;</form>", Form.Model(Empty)["<x>"].ToHtml());
        Assert.Equal(
            "<form><span></span><span></span></form>",
            Form.Model(Empty)[new List<Component?> { Span, Span }].ToHtml());
    }

    // The factory has to run INSIDE the form's children scope, or a bound control built by it resolves
    // to an empty auto-created EditContext and its validators never fire — the exact failure the
    // IEnumerable indexer materialises eagerly to avoid.
    [Fact]
    public void Submit_state_children_resolve_the_forms_EditContext()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        EditContext? seen = null;

        var view = new StubComponent(() => Form.Model(p)[_ => [new ContextCapture(c => seen = c)]]);
        view.RenderAsLiveRoot();

        Assert.NotNull(seen);
        Assert.Same(p, seen!.Model);
    }

    [Fact]
    public async Task Submit_state_children_see_submitting_while_an_async_handler_is_in_flight()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var release = new TaskCompletionSource();
        var seen = new List<bool>();

        var view = new StubComponent(() => Form.Model(p)
            .OnSubmit(async _ => await release.Task)[f =>
        {
            seen.Add(f.Submitting);
            return [];
        }
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = MarkupAssert.Attr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        var pending = view.TryInvokeHandlerAsync(submitId!, doc.RootElement).AsTask();
        view.RenderAsLiveRoot();
        release.SetResult();
        await pending;
        view.RenderAsLiveRoot();

        Assert.False(seen[0]);
        Assert.Contains(true, seen);
        Assert.False(seen[^1]);
    }

    // A handler that throws must not strand the form showing a submit that is no longer running.
    [Fact]
    public async Task Submit_state_children_stop_submitting_when_the_handler_throws()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var seen = new List<bool>();

        var view = new StubComponent(() => Form.Model(p)
            .OnSubmit(_ => throw new InvalidOperationException("boom"))[f =>
        {
            seen.Add(f.Submitting);
            return [];
        }
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = MarkupAssert.Attr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);
        view.RenderAsLiveRoot();

        Assert.False(seen[^1]);
    }

    // The point of catching it: a failed save is something the page RENDERS, not something that takes
    // the handler down. Without this the only way to show the reader anything is a try/catch in every
    // submit, and the one a page forgets is the one that fails silently.
    [Fact]
    public async Task A_handler_that_throws_lands_on_the_forms_error_instead_of_faulting()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var seen = new List<Exception?>();

        var view = new StubComponent(() => Form.Model(p)
            .OnSubmit(_ => throw new InvalidOperationException("boom"))[f =>
        {
            seen.Add(f.Error);
            return [];
        }
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = MarkupAssert.Attr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);
        view.RenderAsLiveRoot();

        Assert.Null(seen[0]);
        var error = Assert.IsType<InvalidOperationException>(seen[^1]);
        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public async Task The_error_is_cleared_as_the_next_submit_starts_not_as_one_ends()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var fail = true;
        var seen = new List<Exception?>();

        var view = new StubComponent(() => Form.Model(p)
            .OnSubmit(_ => fail ? throw new InvalidOperationException("boom") : Task.CompletedTask)[f =>
        {
            seen.Add(f.Error);
            return [];
        }
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = MarkupAssert.Attr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);
        view.RenderAsLiveRoot();
        Assert.NotNull(seen[^1]);

        // The retry: a form showing the last attempt's message beside this one's spinner would be
        // reporting something that is no longer happening.
        fail = false;
        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);
        view.RenderAsLiveRoot();

        Assert.Null(seen[^1]);
    }

    private sealed class Person
    {
        [Required] public string Name { get; set; } = "";
        [Range(1, 120)] public int Age { get; set; }
    }

    private sealed class RejectingAsyncValidator : IAsyncFieldValidator
    {
        public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            context.AddValidationMessage(new FieldIdentifier(context.Model, "Name"), "remote check failed");
        }

        public ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    [Fact]
    public void Rel_emits_after_the_name() =>
        Assert.Contains(
            "name=\"f\" rel=\"noopener\"",
            Form.Model(Empty).Name("f").Target("_blank").Rel("noopener").ToHtml());

}
