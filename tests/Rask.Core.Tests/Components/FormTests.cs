using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class FormTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<form></form>", Form().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<form id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" enctype=\"multipart/form-data\" target=\"_blank\" accept-charset=\"utf-8\" autocomplete=\"off\" novalidate name=\"n\"></form>",
            Form("multipart/form-data", "_blank", "utf-8", "off", true, "n", Id: "i", Class: "c", Style: "s",
                Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<form>&lt;x&gt;</form>", Form()["<x>"].ToHtml());

    [Fact]
    public void Render_OnSubmitOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<form></form>",
            Form(OnSubmit: _ => { }).ToHtml());

    [Fact]
    public void Render_OnSubmitInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => Form(OnSubmit: _ => { }));
        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnSubmitAsyncInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => Form(OnSubmitAsync: async _ => { await Task.Yield(); }));
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

        var view = new StubComponent(() => Form(
            p,
            (Callback<Person>)(_ => validCalled++),
            (Callback<Person>)(_ => invalidCalled++),
            Context: ctx)[Input(() => p.Name), Input(() => p.Age)]);
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
}
