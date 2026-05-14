using System.Text.Json;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Forms;

public class AsyncFormBindingTests
{
    [Fact]
    public async Task ExplicitContext_InputDispatch_RoutesValidationThroughUserContext()
    {
        // Regression: Input's bound factory runs before Form.EnterChildrenScope, so without
        // Form.Context's setter registering with LiveRenderContext, the input would resolve to
        // an auto-created EditContext (no user validators) and the user's pre-built context
        // would never receive the messages.
        var model = new SignupModel { Username = "ada" };
        var ctx = new EditContext(model);
        ctx.AddValidator(new TaggingAsyncValidator("Username", "no good"));

        var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[
            Input(() => model.Username)
        ]);

        var html = view.RenderAsLiveRoot();
        var changeId = ExtractAttr(html, "data-rask-on-change");
        Assert.NotNull(changeId);

        using var doc = JsonDocument.Parse("{\"value\":\"new\"}");
        await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        var fid = new FieldIdentifier(model, "Username");
        Assert.Equal(new[] { "no good" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task AsyncValidator_DispatchAcrossAwait_TogglesIsValidating()
    {
        var model = new SignupModel { Username = "ada" };
        var ctx = new EditContext(model);
        var validator = new GatedAsyncValidator();
        ctx.AddValidator(validator);

        var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[
            Input(() => model.Username)
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = ExtractAttr(html, "data-rask-on-change");

        using var doc = JsonDocument.Parse("{\"value\":\"taken\"}");
        var dispatchTask = view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        await validator.Started.Task;
        var fid = new FieldIdentifier(model, "Username");
        Assert.True(ctx.IsValidating(fid));
        Assert.True(ctx.IsValidatingAny);

        validator.Release.SetResult();
        await dispatchTask;
        Assert.False(ctx.IsValidating(fid));
        Assert.False(ctx.IsValidatingAny);
    }

    [Fact]
    public async Task AsyncValidator_AddsMessage_VisibleOnUserContext_AfterDispatch()
    {
        var model = new SignupModel { Username = "ada" };
        var ctx = new EditContext(model);
        ctx.AddValidator(new RejectIfEqualsValidator("admin", "Already taken."));

        var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[
            Input(() => model.Username)
        ]);
        var html = view.RenderAsLiveRoot();
        var inputId = ExtractAttr(html, "data-rask-on-input");
        var changeId = ExtractAttr(html, "data-rask-on-change");

        // Mirror the browser: OnInput sets the value, OnChange (blur) touches and validates.
        using var inputDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
        await view.TryInvokeHandlerAsync(inputId!, inputDoc.RootElement);
        using var changeDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
        await view.TryInvokeHandlerAsync(changeId!, changeDoc.RootElement);

        var fid = new FieldIdentifier(model, "Username");
        Assert.Equal("admin", model.Username);
        Assert.Equal(new[] { "Already taken." }, ctx.GetValidationMessages(fid));
        Assert.False(ctx.IsValidating(fid));
    }

    [Fact]
    public void ValidatingIndicator_RendersChildren_WhenFieldIsValidating()
    {
        var model = new SignupModel { Username = "ada" };
        var ctx = new EditContext(model);
        var fid = new FieldIdentifier(model, "Username");
        var state = ctx.GetType().GetField("_states", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        // Simulate an in-flight async validation by bumping PendingCount through ValidateFieldAsync.
        // (We don't await; we just observe the indicator's render against a forced pending state.)
        ctx.AddValidator(new NeverCompletingAsyncValidator());
        _ = ctx.ValidateFieldAsync(fid);
        Assert.True(ctx.IsValidating(fid));

        var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[
            ValidatingIndicator(() => model.Username, () => Span(Class: "spinner")["Checking..."])
        ]);

        var html = view.RenderAsLiveRoot();
        Assert.Contains("<span class=\"spinner\">", html);
        Assert.Contains("Checking...", html);
    }

    [Fact]
    public void ValidatingIndicator_RendersNothing_WhenFieldNotValidating()
    {
        var model = new SignupModel { Username = "ada" };
        var ctx = new EditContext(model);

        var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[
            ValidatingIndicator(() => model.Username, () => Span(Class: "spinner")["Checking..."])
        ]);

        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("Checking...", html);
        Assert.DoesNotContain("spinner", html);
    }

    private static string? ExtractAttr(string html, string attr)
    {
        var marker = attr + "=\"";
        var i = html.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var start = i + marker.Length;
        var end = html.IndexOf('"', start);
        return end < 0 ? null : html.Substring(start, end - start);
    }

    private sealed class SignupModel
    {
        public string Username { get; set; } = "";
    }

    private sealed class TaggingAsyncValidator(string fieldName, string message) : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            if (field.FieldName == fieldName)
            {
                context.AddValidationMessage(field, message);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RejectIfEqualsValidator(string reject, string message) : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            await Task.Yield();
            if (context.Model is SignupModel m && string.Equals(m.Username, reject, StringComparison.OrdinalIgnoreCase))
            {
                context.AddValidationMessage(field, message);
            }
        }
    }

    private sealed class GatedAsyncValidator : IAsyncFieldValidator
    {
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource Release { get; } = new();

        public ValueTask ValidateAsync(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
        }
    }

    private sealed class NeverCompletingAsyncValidator : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            await new TaskCompletionSource().Task.ConfigureAwait(false);
        }
    }
}
