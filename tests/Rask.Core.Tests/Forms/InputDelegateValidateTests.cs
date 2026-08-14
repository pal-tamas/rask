using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

public partial class InputDelegateValidateTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Input_InlineValidate_AppendsMessage_OnPerKeystroke()
    {
        var p = new Person { Name = "" };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Name)
                .Validate(v =>
                    v.Length < 3 ? new[] { "too short" } : Array.Empty<string>()),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);
        await page.InvokeAsync(changeId!, "{\"value\":\"ab\"}");

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

        var page = RaskTest.Render(() => Form.Model(p).OnValidSubmit(_ => validCalled++).OnInvalidSubmit(_ => invalidCalled++)[
            Input.Bind(() => p.Name)
                .Validate(_ => new[] { "always-fail" }),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        await page.SubmitAsync("{\"form\":{\"Name\":\"\"}}");

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

        var page = RaskTest.Render(() => Form.Model(p)[
            includeValidator
                ? Input.Bind(() => p.Name)
                    .Validate(v =>
                        v.Length < 3 ? new[] { "too short" } : Array.Empty<string>())
                : Input.Bind(() => p.Name),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        await page.ChangeAsync("{\"value\":\"ab\"}");

        Assert.NotEmpty(captured!.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));

        // Drop the Validate parameter; re-render so the binding factory re-runs with
        // Validate=null, which clears the prior registration. Then fire another change
        // event — no rule, no messages.
        includeValidator = false;
        page.Render();
        await page.ChangeAsync("{\"value\":\"ab\"}");

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

        var page = RaskTest.Render(() => Form.Model(p)[
            Input(() => p.Name,
                async (v, ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return v.Length < 3 ? new[] { "async-too-short" } : Array.Empty<string>();
                }),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

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

        var page = RaskTest.Render(() => Form.Model(p)[
            Input(() => p.Name,
                async (v, ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return Array.Empty<string>();
                }),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        Assert.NotNull(captured);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await captured!.ValidateFieldAsync(new FieldIdentifier(p, nameof(Person.Name)), cts.Token);

        // OCE bubbled — no generic message added; no success messages either.
        Assert.Empty(captured.GetValidationMessages(new FieldIdentifier(p, nameof(Person.Name))));
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
    }
}
