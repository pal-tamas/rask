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
    public void Render_Rel_EmitsTheAttribute() =>
        // MDN lists `rel` on <form>; `action` and `method` are correctly absent, because Rask submits
        // in-process and runs OnValidSubmit/OnInvalidSubmit rather than navigating (#694).
        Assert.Contains("rel=\"noopener\"", Form.Model(Empty).Rel("noopener").ToHtml());

}
