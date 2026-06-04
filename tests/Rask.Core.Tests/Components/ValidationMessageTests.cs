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
                    entries.Select((e, i) => Li(Key: i)[e.Message])
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

        var view = new StubComponent(() => Form(Context: ctx, Model: p)[
            ValidatingIndicator(() => p.Name,
                () => Div(Class: "validating-indicator")["Checking..."])
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

        var view = new StubComponent(() => Form(Context: ctx, Model: p)[
            ValidatingIndicator(() => p.Name,
                () => Div(Class: "validating-indicator")["Checking..."])
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

        var view = new StubComponent(() => Form(Context: ctx, Model: p)[
            ValidatingIndicator(() => p.Name,
                () => Div(Class: "validating-indicator")["Checking..."])
        ]);

        var task = ctx.ValidateFieldAsync(fid);
        view.RenderAsLiveRoot();
        gate.SetResult();
        await task;

        view.RenderAsLiveRoot();  // sticky window starts
        await Task.Delay(80);     // > 30ms sticky window
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

        var view = new StubComponent(() => Form(Context: ctx, Model: p)[
            ValidatingIndicator(() => p.Name,
                () => Div(Class: "validating-indicator")["Checking..."])
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
        public static System.Linq.Expressions.Expression<Func<TProp>> For<TProp>(
            System.Linq.Expressions.Expression<Func<TProp>> expr) => expr;
    }
}
