using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Html.Tests.Components;

public partial class FormTests : global::Rask.Core.RaskMarkup
{
    // These assert what a <form> RENDERS, and a model renders nothing — so binding one leaves every
    // expectation below untouched. `Form` requires it because a form with nothing to bind to has no
    // fields that can resolve, which is a compile error now rather than a throw at first render.
    private static readonly Person Empty = new();

    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<form></form>", Form.Model(Empty).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
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
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<form>&lt;x&gt;</form>", Form.Model(Empty)["<x>"].ToHtml());

    [Fact]
    public void Render_OnSubmitOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<form></form>",
            Form.Model(Empty).OnSubmit(_ => { }).ToHtml());

    [Fact]
    public void Render_OnSubmitInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => Form.Model(Empty).OnSubmit(_ => { }));
        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnSubmitAsyncInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => Form.Model(Empty).OnSubmitAsync(async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public async Task SubmitBridge_AwaitsAsyncValidation_BeforeRouting()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var validCalled = 0;
        var invalidCalled = 0;

        var ctx = new EditContext(p);
        ctx.AddValidator(new RejectingAsyncValidator());

        var view = new StubComponent(() => Form.Model(p)
            .OnValidSubmit(_ => validCalled++)
            .OnInvalidSubmit(_ => invalidCalled++)
            .Context(ctx)[Input.Bind(() => p.Name), Input.Bind(() => p.Age)]);
        var html = view.RenderAsLiveRoot();

        var submitId = Markup.Attr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");
        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);

        Assert.Equal(0, validCalled);
        Assert.Equal(1, invalidCalled);
    }

    [Fact]
    public void Render_SubmitStateChildren_BuildsThemNotSubmitting() =>
        Assert.Equal(
            "<form>idle</form>",
            Form.Model(Empty)[submitting => [submitting ? "busy" : "idle"]].ToHtml());

    // The old syntax is the point of the overload, not a side effect of it: a fixed list still binds to
    // the typed indexer, and a bare string still reaches the loose one as ONE text child rather than one
    // child per character. A lambda whose parameter is untyped and whose body is a collection expression
    // has no natural type, which is what keeps it out of the `params object?[]` overload's way.
    [Fact]
    public void Render_FixedChildren_StillBindToTheListIndexers()
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
    public void SubmitStateChildren_ResolveTheFormsEditContext()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        EditContext? seen = null;

        var view = new StubComponent(() => Form.Model(p)[_ => [new ContextCapture(c => seen = c)]]);
        view.RenderAsLiveRoot();

        Assert.NotNull(seen);
        Assert.Same(p, seen!.Model);
    }

    [Fact]
    public async Task SubmitStateChildren_SeeSubmitting_WhileAnAsyncHandlerIsInFlight()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var release = new TaskCompletionSource();
        var seen = new List<bool>();

        var view = new StubComponent(() => Form.Model(p)
            .OnValidSubmitAsync(async _ => await release.Task)[submitting =>
        {
            seen.Add(submitting);
            return [];
        }
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = Markup.Attr(html, "data-rask-on-submit");
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
    public async Task SubmitStateChildren_StopSubmitting_WhenTheHandlerThrows()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var seen = new List<bool>();

        var view = new StubComponent(() => Form.Model(p)
            .OnValidSubmitAsync(_ => throw new InvalidOperationException("boom"))[submitting =>
        {
            seen.Add(submitting);
            return [];
        }
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = Markup.Attr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => view.TryInvokeHandlerAsync(submitId!, doc.RootElement).AsTask());

        view.RenderAsLiveRoot();
        Assert.False(seen[^1]);
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
    public void Render_Rel_EmitsAfterName() =>
        Assert.Contains(
            "name=\"f\" rel=\"noopener\"",
            Form.Model(Empty).Name("f").Target("_blank").Rel("noopener").ToHtml());

}
