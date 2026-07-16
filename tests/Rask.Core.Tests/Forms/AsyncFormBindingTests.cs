using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Routing;

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

        var page = RaskTest.Render(() => Form<SignupModel>(model, Context: ctx)[
            Input(() => model.Username)
        ]);

        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);

        await page.InvokeAsync(changeId!, "{\"value\":\"new\"}");

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

        var page = RaskTest.Render(() => Form<SignupModel>(model, Context: ctx)[
            Input(() => model.Username)
        ]);
        var changeId = page.HandlerId("change");

        // Held un-awaited on purpose: the gate keeps the validator mid-flight so the state below is
        // observable. InvokeAsync returning a Task is what makes that expressible through the public API.
        var dispatchTask = page.InvokeAsync(changeId!, "{\"value\":\"taken\"}");

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

        var page = RaskTest.Render(() => Form<SignupModel>(model, Context: ctx)[
            Input(() => model.Username)
        ]);

        // Mirror the browser: OnInput sets the value, OnChange (blur) touches and validates.
        await page.InputAsync("{\"value\":\"admin\"}");
        await page.ChangeAsync("{\"value\":\"admin\"}");

        var fid = new FieldIdentifier(model, "Username");
        Assert.Equal("admin", model.Username);
        Assert.Equal(new[] { "Already taken." }, ctx.GetValidationMessages(fid));
        Assert.False(ctx.IsValidating(fid));
    }

    // The two PostHandlerRender tests below stay on the internal render entry points, deliberately.
    // They install a RenderingHandle so the dispatcher's mid-await render produces a real cached
    // subtree — that cache is the thing under test, and RaskTest has no RenderHandle to install
    // (nor should it: a render handle is a live-session mechanism, below the HTML + dispatch seam
    // the package covers). They pass without the handle too, which is exactly the trap: the
    // assertions survive while the cached-subtree path they exist to cover quietly stops running.
    [Fact]
    public async Task AsyncValidator_PostHandlerRender_ShowsMessage_AndNoIndicator()
    {
        // Mirrors the failing E2E test Validation_AsyncDemo_ShowsCheckingThenTakenMessage:
        // fill the bound input with "admin" and fire OnChange (blur). After the async validator
        // completes, the next RenderAsLiveRoot — i.e. what the WS dispatcher emits as the
        // post-handler frame — must show the "taken" message and must NOT still show the
        // "Checking…" indicator.
        var model = new SignupModel { Username = "" };
        // Opt out of the ValidatingIndicator sticky window so this test stays
        // a strict assertion on the post-handler frame — sticky is a UI smoothing
        // feature and overlaps the "no indicator after validation" check the test
        // is pinning. Pre-sticky behaviour is preserved when ValidatingStickyMs=0.
        var ctx = new EditContext(model) { ValidatingStickyMs = 0 };
        ctx.AddValidator(new DelayedRejectValidator("admin", "Already taken.", 20));

        var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[
            Input(() => model.Username),
            ValidatingIndicator(() => model.Username, () => Span(Class: "spinner")["Checking..."]),
            ValidationMessage(() => model.Username, msgs => Div(Class: "text-danger")[msgs[0]])
        ]);
        var handle = new RenderingHandle(view);
        view.RenderHandle = handle;

        var initial = view.RenderAsLiveRoot();
        var inputId = Markup.Attr(initial, "data-rask-on-input");
        var changeId = Markup.Attr(initial, "data-rask-on-change");

        using var inputDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
        await view.TryInvokeHandlerAsync(inputId!, inputDoc.RootElement);
        using var changeDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
        await view.TryInvokeHandlerAsync(changeId!, changeDoc.RootElement);

        var fid = new FieldIdentifier(model, "Username");
        Assert.Equal(new[] { "Already taken." }, ctx.GetValidationMessages(fid));
        Assert.False(ctx.IsValidating(fid));

        // The actual bug: the post-handler render — the next RenderAsLiveRoot call — must
        // reflect those facts in the emitted HTML.
        var post = view.RenderAsLiveRoot();
        Assert.Contains("Already taken.", post);
        Assert.DoesNotContain("Checking...", post);
    }

    [Fact]
    public async Task AsyncValidator_PostHandlerRender_UnderRouterOutlet_ShowsMessage_AndNoIndicator()
    {
        // Same reproduction as the StubComponent test above, but with the validation page
        // nested inside a Router/Outlet chain — i.e. the structure the real showcase uses.
        // The recent commit 474bcf4 removed BypassRenderCache from Router and Outlet, so
        // when no nav happens the route subtree is served from cache; this test pins that
        // the post-handler render still walks down to the dirty AsyncValidationDemo and
        // produces the validator's terminal output rather than a stale "Checking..." frame.
        var state = new RouteState { Path = "/form" };
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var sp = services.BuildServiceProvider();

        var view = new StubComponent(() => Router(new[] { Route<RouterOutletFormPage>("/form") }));
        var handle = new RenderingHandle(view);
        view.RenderHandle = handle;

        var initial = view.RenderAsLiveRoot(sp);
        var inputId = Markup.Attr(initial, "data-rask-on-input");
        var changeId = Markup.Attr(initial, "data-rask-on-change");
        Assert.NotNull(inputId);
        Assert.NotNull(changeId);

        using var inputDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
        await view.TryInvokeHandlerAsync(inputId!, inputDoc.RootElement, sp);
        using var changeDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
        await view.TryInvokeHandlerAsync(changeId!, changeDoc.RootElement, sp);

        var post = view.RenderAsLiveRoot(sp);
        Assert.Contains("Already taken.", post);
        Assert.DoesNotContain("Checking...", post);
    }

    [Fact]
    public void ValidatingIndicator_RendersChildren_WhenFieldIsValidating()
    {
        var model = new SignupModel { Username = "ada" };
        var ctx = new EditContext(model);
        var fid = new FieldIdentifier(model, "Username");
        var state = ctx.GetType().GetField("_states", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Simulate an in-flight async validation by bumping PendingCount through ValidateFieldAsync.
        // (We don't await; we just observe the indicator's render against a forced pending state.)
        ctx.AddValidator(new NeverCompletingAsyncValidator());
        _ = ctx.ValidateFieldAsync(fid);
        Assert.True(ctx.IsValidating(fid));

        var html = RaskTest.Render(() => Form<SignupModel>(model, Context: ctx)[
            ValidatingIndicator(() => model.Username, () => Span(Class: "spinner")["Checking..."])
        ]).Html;

        Assert.Contains("<span class=\"spinner\">", html);
        Assert.Contains("Checking...", html);
    }

    [Fact]
    public void ValidatingIndicator_RendersNothing_WhenFieldNotValidating()
    {
        var model = new SignupModel { Username = "ada" };
        var ctx = new EditContext(model);

        var html = RaskTest.Render(() => Form<SignupModel>(model, Context: ctx)[
            ValidatingIndicator(() => model.Username, () => Span(Class: "spinner")["Checking..."])
        ]).Html;

        Assert.DoesNotContain("Checking...", html);
        Assert.DoesNotContain("spinner", html);
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

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken ct) =>
            await new TaskCompletionSource().Task.ConfigureAwait(false);
    }

    private sealed class DelayedRejectValidator(string reject, string message, int delayMs) : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            if (context.Model is SignupModel m && string.Equals(m.Username, reject, StringComparison.OrdinalIgnoreCase))
            {
                context.AddValidationMessage(field, message);
            }
        }
    }

    // Minimal IRenderHandle: fires a fresh RenderAsLiveRoot on every render request so the
    // dispatcher's mid-await render path produces a real cached subtree, the same way the
    // server's LiveSession.RenderInScopeAsync does.
    private sealed class RenderingHandle(Component view) : IRenderHandle
    {
        public Task RequestRenderAsync()
        {
            view.RenderAsLiveRoot();
            return Task.CompletedTask;
        }

        Task IRenderHandle.RenderInScopeAsync()
        {
            view.RenderAsLiveRoot();
            return Task.CompletedTask;
        }
    }

    [SkipFactory]
    public sealed class RouterOutletFormPage : Component
    {
        private readonly EditContext _ctx;
        private readonly SignupModel _model = new();

        public RouterOutletFormPage()
        {
            // ValidatingStickyMs=0 keeps the test a strict "no indicator after
            // PendingCount drops to 0" assertion. See sibling test above.
            _ctx = new EditContext(_model) { ValidatingStickyMs = 0 };
            _ctx.AddValidator(new DelayedRejectValidator("admin", "Already taken.", 20));
        }

        protected override Component? Render() =>
            Form<SignupModel>(_model, Context: _ctx)[
                Input(() => _model.Username),
                ValidatingIndicator(() => _model.Username, () => Span(Class: "spinner")["Checking..."]),
                ValidationMessage(() => _model.Username, msgs => Div(Class: "text-danger")[msgs[0]])
            ];
    }
}
