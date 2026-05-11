using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Tests.Live;
using static Rask.Core.Tags;

namespace Rask.Core.Tests.Forms;

public class FormBindingTests
{
    [Fact]
    public void BoundInput_RendersValueFromGetter_AndAutoNamesField()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var view = new StubComponent(() => Form(p, Children:
        [
            Input(() => p.Name),
            Input(() => p.Age),
            Input(() => p.Subscribed)
        ]));

        var html = view.RenderAsLiveRoot();

        Assert.Contains("name=\"Name\"", html);
        Assert.Contains("value=\"Ada\"", html);
        Assert.Contains("type=\"number\"", html);
        Assert.Contains("name=\"Age\"", html);
        Assert.Contains("value=\"30\"", html);
        Assert.Contains("type=\"checkbox\"", html);
    }

    [Fact]
    public async Task OnInput_UpdatesBoundStringField_DuringInputEvent()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var view = new StubComponent(() => Form(p, Children:
        [
            Input(() => p.Name)
        ]));
        var html = view.RenderAsLiveRoot();

        var inputId = ExtractAttr(html, "data-rask-on-input");
        Assert.NotNull(inputId);

        using var doc = JsonDocument.Parse("{\"value\":\"Bea\"}");
        var ok = await view.TryInvokeHandlerAsync(inputId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal("Bea", p.Name);
    }

    [Fact]
    public async Task OnChange_UpdatesNumericBoundField_AndMarksTouched()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var view = new StubComponent(() => Form(p, Children:
        [
            Input(() => p.Age)
        ]));
        var html = view.RenderAsLiveRoot();

        var changeId = ExtractAttr(html, "data-rask-on-change");
        Assert.NotNull(changeId);

        using var doc = JsonDocument.Parse("{\"value\":\"42\"}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(42, p.Age);
    }

    [Fact]
    public async Task Submit_InvalidModel_CallsOnInvalidSubmit_NotOnValidSubmit()
    {
        var p = new Person { Name = "", Age = 0 };
        var validCalled = 0;
        var invalidCalled = 0;

        var view = new StubComponent(() => Form(
            p,
            (Action<Person>)(_ => validCalled++),
            (Action<Person>)(_ => invalidCalled++),
            Children: [Input(() => p.Name), Input(() => p.Age)]));
        var html = view.RenderAsLiveRoot();

        var submitId = ExtractAttr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"\",\"Age\":\"0\"}}");
        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);

        Assert.Equal(0, validCalled);
        Assert.Equal(1, invalidCalled);
    }

    [Fact]
    public async Task Submit_ValidModel_CallsOnValidSubmitWithPopulatedModel()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        Person? captured = null;

        var view = new StubComponent(() => Form(
            p,
            (Action<Person>)(m => captured = m),
            Children: [Input(() => p.Name), Input(() => p.Age)]));
        var html = view.RenderAsLiveRoot();

        var submitId = ExtractAttr(html, "data-rask-on-submit");
        using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");
        await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);

        Assert.Same(p, captured);
        Assert.Equal("Ada", captured!.Name);
    }

    [Fact]
    public void EditContext_PersistsAcrossRenders_ForSameModel()
    {
        var p = new Person { Name = "", Age = 30 };
        var captures = new List<EditContext>();

        var view = new StubComponent(() => Form(p, Children:
        [
            new ContextCapture(captures.Add)
        ]));
        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

        Assert.Equal(2, captures.Count);
        Assert.Same(captures[0], captures[1]);
    }

    private static string? ExtractAttr(string html, string attr)
    {
        var marker = attr + "=\"";
        var i = html.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        var start = i + marker.Length;
        var end = html.IndexOf('"', start);
        return end < 0 ? null : html.Substring(start, end - start);
    }

    private sealed class Person
    {
        [Required] public string Name { get; set; } = "";
        [Range(1, 120)] public int Age { get; set; }
        public bool Subscribed { get; set; }
    }

    private sealed class ContextCapture(Action<EditContext> capture) : Component
    {
        public override Component Render()
        {
            if (EditContextScope.Current is { } c)
            {
                capture(c);
            }

            return new Fragment();
        }
    }
}
