using System.Linq.Expressions;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Html.Tests.Components;

public partial class ValidationMessageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void OutsideEditContext_RendersNothing()
    {
        var p = new Person();
        var html = ValidationMessage.Template(msgs => Div.Class("validation-message")[msgs[0]]).For(() => p.Name).ToHtml();
        Assert.Equal("", html);
    }

    [Fact]
    public void InsideEditContext_NoMessages_RendersNothing()
    {
        var p = new Person { Name = "Ada" };
        var view = new StubComponent(() => Form.Model(p)[
            ValidationMessage.Template(msgs => Div.Class("validation-message")[msgs[0]]).For(() => p.Name)
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

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidationMessage.Template(msgs => Div.Class("validation-message")[msgs[0]]).For(() => p.Name)
        ]);
        var html = view.RenderAsLiveRoot();

        Assert.Contains("class=\"validation-message\"", html);
        Assert.Contains("Name is required", html);
    }

    [Fact]
    public void ValidationMessage_MessageAddedAfterFirstRender_RepaintsOnReRender_ViaAutoLatch()
    {
        // ValidationMessage carries no manual BypassRenderCache override anymore. Its first render
        // reads EditContext.GetValidationMessages (no messages yet) and populates the render cache with
        // the empty result; that read auto-latches the component as a cache opt-out (EditContext
        // .MarkReader -> Component._readsAmbientState). So when a message is added out-of-band and the
        // same pooled view is re-rendered with nothing marked prop/state-dirty, the second walk must
        // still re-execute Render() and surface the message rather than serve the stale empty frame.
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);
        var field = new FieldIdentifier(p, nameof(Person.Name));

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidationMessage.Template(msgs => Div.Class("validation-message")[msgs[0]]).For(() => p.Name)
        ]);

        var first = view.RenderAsLiveRoot();
        Assert.DoesNotContain("validation-message", first);

        ctx.AddValidationMessage(field, "Name is required");

        var second = view.RenderAsLiveRoot();
        Assert.Contains("class=\"validation-message\"", second);
        Assert.Contains("Name is required", second);
    }

    [Fact]
    public void ValidationSummary_MessageAddedAfterFirstRender_RepaintsOnReRender_ViaAutoLatch()
    {
        // Same auto-latch guarantee for the GetValidationEntries read path (ValidationSummary). It
        // must start non-empty so the first render caches a non-null <ul> (a null/empty render is never
        // cached and would repaint regardless, proving nothing): render one message, add a second
        // out-of-band, and require the stale one-item cache to be replaced by the two-item summary.
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);
        var name = new FieldIdentifier(p, nameof(Person.Name));
        var email = new FieldIdentifier(p, nameof(Person.Email));
        ctx.AddValidationMessage(name, "Name is required");

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidationSummary
                .Template(entries =>
                Ul.Class("validation-summary")[
                    entries.Select((e, i) => Li.Key(i)[e.Message])
                ])
        ]);

        var first = view.RenderAsLiveRoot();
        Assert.Contains("Name is required", first);
        Assert.DoesNotContain("Email is required", first);

        ctx.AddValidationMessage(email, "Email is required");

        var second = view.RenderAsLiveRoot();
        Assert.Contains("Name is required", second);
        Assert.Contains("Email is required", second);
    }

    [Fact]
    public void ValidationSummary_WithMessages_RendersTemplate()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);
        ctx.AddValidationMessage(new FieldIdentifier(p, nameof(Person.Name)), "Name is required");

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidationSummary
                .Template(entries =>
                Ul.Class("validation-summary")[
                    entries.Select((e, i) => Li.Key(i)[e.Message])
                ])
        ]);
        var html = view.RenderAsLiveRoot();

        Assert.Contains("<ul class=\"validation-summary\">", html);
        Assert.Contains("Name is required", html);
    }

    [Fact]
    public async Task ValidatingIndicator_PendingCountTrue_RendersTemplate()
    {
        var p = new Person();
        var fid = new FieldIdentifier(p, nameof(Person.Name));
        var ctx = new EditContext(p);
        var gate = new TaskCompletionSource();
        ctx.AddValidator(new GatedValidator(gate.Task));

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidatingIndicator.Template(() => Div.Class("validating-indicator")["Checking..."])
                .For(() => p.Name)
        ]);

        // Kick off validation so PendingCount > 0.
        var task = ctx.ValidateFieldAsync(fid);
        var html = view.RenderAsLiveRoot();

        Assert.Contains("class=\"validating-indicator\"", html);
        Assert.Contains("Checking...", html);

        // Let the validator settle so xUnit doesn't see a leaked task.
        gate.SetResult();
        await task;
    }

    [Fact]
    public async Task ValidatingIndicator_AfterPendingDropsToZero_StaysRenderedForStickyWindow()
    {
        // The sticky window keeps the indicator in the DOM after the validator
        // completes so a sub-second visible window (one of FluentValidation's
        // MustAsync or a similar 400ms async check) is observable to screen-
        // readers and Playwright polling under load.
        var p = new Person();
        var fid = new FieldIdentifier(p, nameof(Person.Name));
        var ctx = new EditContext(p);
        var gate = new TaskCompletionSource();
        ctx.AddValidator(new GatedValidator(gate.Task));

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidatingIndicator.Template(() => Div.Class("validating-indicator")["Checking..."])
                .For(() => p.Name)
        ]);

        var task = ctx.ValidateFieldAsync(fid);
        var validatingHtml = view.RenderAsLiveRoot();
        Assert.Contains("validating-indicator", validatingHtml);

        // Complete the validator so PendingCount drops to 0. The indicator
        // stays rendered for the sticky window via ShouldShowValidatingIndicator
        // even though IsValidating (which is strict on PendingCount) returns
        // false right away.
        gate.SetResult();
        await task;
        Assert.False(ctx.IsValidating(fid));
        Assert.True(ctx.ShouldShowValidatingIndicator(fid),
            "Sticky window should keep ShouldShowValidatingIndicator true.");

        var stickyHtml = view.RenderAsLiveRoot();
        Assert.Contains("validating-indicator", stickyHtml);
    }

    [Fact]
    public async Task ValidatingIndicator_AfterStickyWindowExpires_NoLongerRenders()
    {
        var p = new Person();
        var fid = new FieldIdentifier(p, nameof(Person.Name));
        var ctx = new EditContext(p) { ValidatingStickyMs = 30 };
        var gate = new TaskCompletionSource();
        ctx.AddValidator(new GatedValidator(gate.Task));

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidatingIndicator.Template(() => Div.Class("validating-indicator")["Checking..."])
                .For(() => p.Name)
        ]);

        var task = ctx.ValidateFieldAsync(fid);
        view.RenderAsLiveRoot();
        gate.SetResult();
        await task;

        view.RenderAsLiveRoot(); // sticky window starts
        await Task.Delay(80); // > 30ms sticky window
        var finalHtml = view.RenderAsLiveRoot();
        Assert.DoesNotContain("validating-indicator", finalHtml);
    }

    [Fact]
    public async Task ValidatingIndicator_StickyMsZero_RemovesImmediately_AfterPendingDropsToZero()
    {
        // Legacy callers that want the prior "render only while PendingCount > 0"
        // behaviour set ValidatingStickyMs=0 — proves the opt-out works.
        var p = new Person();
        var fid = new FieldIdentifier(p, nameof(Person.Name));
        var ctx = new EditContext(p) { ValidatingStickyMs = 0 };
        var gate = new TaskCompletionSource();
        ctx.AddValidator(new GatedValidator(gate.Task));

        var view = new StubComponent(() => Form.Model(p).Context(ctx)[
            ValidatingIndicator.Template(() => Div.Class("validating-indicator")["Checking..."])
                .For(() => p.Name)
        ]);

        var task = ctx.ValidateFieldAsync(fid);
        view.RenderAsLiveRoot();
        gate.SetResult();
        await task;

        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("validating-indicator", html);
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    private sealed class GatedValidator(Task wait) : IAsyncFieldValidator
    {
        public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
            => await wait.ConfigureAwait(false);

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
            CancellationToken cancellationToken)
            => await wait.ConfigureAwait(false);
    }

    private static class TestExpressions
    {
        public static Expression<Func<TProp>> For<TProp>(
            Expression<Func<TProp>> expr) => expr;
    }
}
