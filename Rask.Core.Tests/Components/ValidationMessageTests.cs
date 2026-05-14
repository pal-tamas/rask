using Rask.Core.Forms;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class ValidationMessageTests
{
    [Fact]
    public void OutsideEditContext_RendersNothing()
    {
        var p = new Person();
        var html = ValidationMessage(() => p.Name, msgs => Div(Class: "validation-message")[msgs[0]]).ToHtml();
        Assert.Equal("", html);
    }

    [Fact]
    public void InsideEditContext_NoMessages_RendersNothing()
    {
        var p = new Person { Name = "Ada" };
        var view = new StubComponent(() => Form(p)[
            ValidationMessage(() => p.Name, msgs => Div(Class: "validation-message")[msgs[0]])
        ]);
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("validation-message", html);
    }

    [Fact]
    public void InsideEditContext_WithMessage_RendersTemplate()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);
        ctx.AddValidationMessage(new FieldIdentifier(p, nameof(Person.Name)), "Name is required");

        var view = new StubComponent(() => Form(Context: ctx, Model: p)[
            ValidationMessage(() => p.Name, msgs => Div(Class: "validation-message")[msgs[0]])
        ]);
        var html = view.RenderAsLiveRoot();

        Assert.Contains("class=\"validation-message\"", html);
        Assert.Contains("Name is required", html);
    }

    [Fact]
    public void ValidationSummary_WithMessages_RendersTemplate()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);
        ctx.AddValidationMessage(new FieldIdentifier(p, nameof(Person.Name)), "Name is required");

        var view = new StubComponent(() => Form(Context: ctx, Model: p)[
            ValidationSummary(entries =>
                Ul(Class: "validation-summary")[
                    entries.Select(e => (Child)Li()[e.Message]).ToArray()
                ])
        ]);
        var html = view.RenderAsLiveRoot();

        Assert.Contains("<ul class=\"validation-summary\">", html);
        Assert.Contains("Name is required", html);
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
    }
}
