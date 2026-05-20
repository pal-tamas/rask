using System.Text.Json;
using Rask.Core.Forms;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

public class InputDelegateValidateTests
{
    [Fact]
    public async Task Input_InlineValidate_AppendsMessage_OnPerKeystroke()
    {
        var p = new Person { Name = "" };
        EditContext? captured = null;

        var view = new StubComponent(() => Form(p)[
            Input(() => p.Name,
                v =>
                    v.Length < 3 ? new[] { "too short" } : Array.Empty<string>()),
            new ContextCapture(ctx => captured = ctx)
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = ExtractAttr(html, "data-rask-on-change");
        Assert.NotNull(changeId);
        using var blur = JsonDocument.Parse("{\"value\":\"ab\"}");
        await view.TryInvokeHandlerAsync(changeId!, blur.RootElement);

        Assert.NotNull(captured);
        Assert.Contains("too short",
            captured!.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));
    }

    [Fact]
    public async Task Input_InlineValidate_FiresOnSubmit_EvenWithoutPriorTouch()
    {
        // Reproduces the user's report: the inline `Validate:` on a field must run during
        // the form's submit pipeline regardless of whether the field was ever touched, so
        // that "submit untouched form" still gates OnValidSubmit on the field's rule.
        var p = new Person { Name = "" };
        var validCalled = 0;
        var invalidCalled = 0;
        EditContext? captured = null;

        var view = new StubComponent(() => Form<Person>(
            p,
            _ => validCalled++,
            _ => invalidCalled++)[
            Input(() => p.Name,
                _ => new[] { "always-fail" }),
            new ContextCapture(ctx => captured = ctx)
        ]);
        var html = view.RenderAsLiveRoot();

        var submitId = ExtractAttr(html, "data-rask-on-submit")!;
        using var payload = JsonDocument.Parse("{\"form\":{\"Name\":\"\"}}");
        await view.TryInvokeHandlerAsync(submitId, payload.RootElement);

        Assert.NotNull(captured);
        Assert.Contains("always-fail",
            captured!.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));
        Assert.Equal(0, validCalled);
        Assert.Equal(1, invalidCalled);
    }

    [Fact]
    public async Task Input_InlineValidate_NullOnReRender_ClearsRegistration()
    {
        var p = new Person { Name = "" };
        var includeValidator = true;
        EditContext? captured = null;

        var view = new StubComponent(() => Form(p)[
            includeValidator
                ? Input(() => p.Name,
                    v =>
                        v.Length < 3 ? new[] { "too short" } : Array.Empty<string>())
                : Input(() => p.Name),
            new ContextCapture(ctx => captured = ctx)
        ]);

        var html = view.RenderAsLiveRoot();
        var changeId = ExtractAttr(html, "data-rask-on-change")!;
        using var blur = JsonDocument.Parse("{\"value\":\"ab\"}");
        await view.TryInvokeHandlerAsync(changeId, blur.RootElement);

        Assert.NotEmpty(captured!.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));

        // Drop the Validate parameter; re-render so the binding factory re-runs with
        // Validate=null, which clears the prior registration. Then fire another change
        // event — no rule, no messages.
        includeValidator = false;
        var html2 = view.RenderAsLiveRoot();
        var changeId2 = ExtractAttr(html2, "data-rask-on-change")!;
        using var blur2 = JsonDocument.Parse("{\"value\":\"ab\"}");
        await view.TryInvokeHandlerAsync(changeId2, blur2.RootElement);

        Assert.Empty(captured.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));
    }

    [Fact]
    public async Task Input_InlineValidate_AsyncOverload_RunsThroughValidateAsync()
    {
        // Drives the async `Validate: (v, ct) => …` overload — a one-line lambda binds straight
        // to Func<string, CancellationToken, ValueTask<IEnumerable<string>>> with no cast. The
        // dispatch goes through DelegateValidator.InvokeAsync because IsAsync(d) sees the two
        // parameters; messages land via EditContext.ValidateFieldAsync.
        var p = new Person { Name = "" };
        EditContext? captured = null;

        var view = new StubComponent(() => Form(p)[
            Input(() => p.Name,
                async (v, ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return v.Length < 3 ? new[] { "async-too-short" } : Array.Empty<string>();
                }),
            new ContextCapture(ctx => captured = ctx)
        ]);
        var html = view.RenderAsLiveRoot();
        Assert.NotNull(captured);

        await captured!.ValidateFieldAsync(new FieldIdentifier(p, nameof(Person.Name)));

        Assert.Contains("async-too-short",
            captured.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));
    }

    [Fact]
    public async Task Input_InlineValidate_AsyncOverload_RespectsCancellation()
    {
        // The framework hands the field's own CancellationToken to the async delegate; if the
        // delegate throws OperationCanceledException the run is treated as superseded and no
        // generic "could not be completed" message gets emitted.
        var p = new Person { Name = "abc" };
        EditContext? captured = null;

        var view = new StubComponent(() => Form(p)[
            Input(() => p.Name,
                async (v, ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return Array.Empty<string>();
                }),
            new ContextCapture(ctx => captured = ctx)
        ]);
        view.RenderAsLiveRoot();
        Assert.NotNull(captured);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await captured!.ValidateFieldAsync(new FieldIdentifier(p, nameof(Person.Name)), cts.Token);

        // OCE bubbled — no generic message added; no success messages either.
        Assert.Empty(captured.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));
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
        public string Name { get; set; } = "";
    }

    private sealed class ContextCapture(Action<EditContext> capture) : Component
    {
        protected override Component Render()
        {
            if (EditContextScope.Current is { } c)
            {
                capture(c);
            }

            return Fragment();
        }
    }
}
