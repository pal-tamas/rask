using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class ValidationMessageTests
{
    [Fact]
    public void OutsideEditContext_RendersNothing()
    {
        var p = new Person();
        var html = ValidationMessage(() => p.Name).ToHtml();
        Assert.Equal("", html);
    }

    [Fact]
    public void InsideEditContext_NoMessages_RendersNothing()
    {
        var p = new Person { Name = "Ada" };
        var view = new StubComponent(() => Form(p, Children:
        [
            ValidationMessage(() => p.Name)
        ]));
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("validation-message", html);
    }

    [Fact]
    public void InsideEditContext_AfterValidate_RendersDivPerMessage()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);
        ctx.AddValidator(new DataAnnotationsValidator());
        ctx.Validate();

        var view = new StubComponent(() => Form(Context: ctx, Model: p, Children:
        [
            ValidationMessage(() => p.Name)
        ]));
        var html = view.RenderAsLiveRoot();

        Assert.Contains("class=\"validation-message\"", html);
        Assert.Contains("Name is required", html);
    }

    [Fact]
    public void ValidationSummary_AfterValidate_RendersUlOfMessages()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);
        ctx.AddValidator(new DataAnnotationsValidator());
        ctx.Validate();

        var view = new StubComponent(() => Form(Context: ctx, Model: p, Children:
        [
            ValidationSummary()
        ]));
        var html = view.RenderAsLiveRoot();

        Assert.Contains("<ul class=\"validation-summary\">", html);
        Assert.Contains("Name is required", html);
    }

    private sealed class Person
    {
        [Required(ErrorMessage = "Name is required")]
        public string Name { get; set; } = "";
    }
}
