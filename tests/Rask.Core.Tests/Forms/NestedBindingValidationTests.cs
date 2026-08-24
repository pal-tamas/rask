using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// Pins that per-keystroke / on-blur validation fires for nested-model bindings (e.g.
// Input.Bind(() => model.Address.Street)) the same way it does for root-model bindings.
//
// The contract: Form.Model (and Form.Context) eagerly walk the model graph at setter time
// and register the form's EditContext under every sub-object's ObjectKey via
// LiveRenderContext.RegisterEditContextForKey. Without that, an Input factory bound to a
// nested chain resolves BindingHelpers.ResolveBindingContext(acc.Target) against the
// sub-object reference and ends up with a *separate* empty EditContext, so handler-driven
// NotifyFieldChanged/ValidateField calls land in a different context than the validators
// (which self-register into EditContextScope.Current during Render) and ValidationMessage
// (which reads EditContextScope.Current). The pre-walk closes that gap.
public partial class NestedBindingValidationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task InlineValidate_NestedField_FiresOnChange()
    {
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Address.Street)
                .Validate(v =>
                    string.IsNullOrEmpty(v) ? new[] { "street required" } : Array.Empty<string>()),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);
        await page.InvokeAsync(changeId!, "{\"value\":\"\"}");

        Assert.NotNull(captured);
        Assert.Contains("street required",
            captured!.GetValidationMessages(new FieldIdentifier(p.Address, nameof(Address.Street))));
    }

    [Fact]
    public async Task InlineValidate_NestedField_ReValidatesOnKeystroke_AfterTouch()
    {
        // Mirrors the "fire on every keystroke once touched" contract for root-model strings:
        // after the first OnChange (blur) touches the field and runs validation, subsequent
        // OnInput events re-validate so a correction clears the message without another blur.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Address.Street)
                .Validate(v =>
                    v.Length < 3 ? new[] { "too short" } : Array.Empty<string>()),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);
        var fid = new FieldIdentifier(p.Address, nameof(Address.Street));

        // Blur with empty — touches and produces the message.
        await page.ChangeAsync("{\"value\":\"\"}");
        Assert.Contains("too short", captured!.GetValidationMessages(fid));

        // Keystroke with a longer value — re-validates because the field is touched.
        await page.InputAsync("{\"value\":\"Oak\"}");
        Assert.Empty(captured.GetValidationMessages(fid));
        Assert.Equal("Oak", p.Address.Street);
    }

    [Fact]
    public async Task InlineValidate_NestedField_LandsInFormsEditContext_NotASeparateOne()
    {
        // The regression this fix targets: without the model-graph pre-walk, the Input handler
        // wrote to a separate EditContext keyed by p.Address. ContextCapture (rendered inside
        // the Form) reads EditContextScope.Current — i.e. the form's context. If the message
        // shows up there, the two contexts are unified.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Address.Street)
                .Validate(_ => new[] { "always-fail" }),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        await page.ChangeAsync("{\"value\":\"x\"}");

        // The form's captured EditContext.Model is the root, not the sub-object.
        Assert.NotNull(captured);
        Assert.Same(p, captured!.Model);
        Assert.Contains("always-fail",
            captured.GetValidationMessages(new FieldIdentifier(p.Address, nameof(Address.Street))));
    }

    [Fact]
    public async Task ModelAndExplicitContext_NestedField_HandlerLandsOnSuppliedContext()
    {
        // Pins the order-sensitive interaction between Form.Model and Form.Context setters when
        // a caller passes BOTH: the generated factory assigns Model first (auto-creating an
        // EditContext keyed by Model and stamping every sub-object → that context), then assigns
        // Context (which overwrites the root-key entry but leaves the sub-object entries pointing
        // at the auto-created context unless RegisterEditContextForKey is willing to overwrite).
        // Without a re-stamp the nested handler resolves to the stray auto-created context, not
        // the user's supplied one — the form's validators never see the field change.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };
        var ctx = new EditContext(p);

        var page = RaskTest.Render(() => Form.Model(p).Context(ctx)[
            Input.Bind(() => p.Address.Street)
                .Validate(_ => new[] { "model-plus-context" }),
            ValidationMessage.Template(msgs => [.. msgs.Select((m, i) => Div.Class("err").Key(i)[m])])
                .For(() => p.Address.Street)
        ]);

        var initial = page.Render();
        var changeId = page.HandlerId("change");
        await page.InvokeAsync(changeId!, "{\"value\":\"x\"}");

        var afterBlur = page.Render();
        Assert.Contains("model-plus-context", afterBlur);
        Assert.Contains("model-plus-context",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address, nameof(Address.Street))));
    }

    [Fact]
    public async Task ExplicitContextForm_NestedField_FiresOnChange()
    {
        // Mirrors the test above but uses the `Context:` overload — the Context setter must
        // also walk the graph so descendant nested inputs resolve to the supplied context.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };
        var ctx = new EditContext(p);
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form.Model(p).Context(ctx)[
            Input.Bind(() => p.Address.Street)
                .Validate(_ => new[] { "nested-explicit-ctx" }),
            RaskTest.EditContextProbe(c => captured = c)
        ]);

        await page.ChangeAsync("{\"value\":\"x\"}");

        Assert.Same(ctx, captured);
        Assert.Contains("nested-explicit-ctx",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address, nameof(Address.Street))));
    }

    [Fact]
    public async Task NestedField_SubObjectReplacedBetweenRenders_NewInstanceValidates()
    {
        // If model.Address is reassigned between renders, the new sub-object must be
        // registered under the form's EditContext on the next render so its bindings still
        // resolve correctly. The Model setter re-walks the graph each render.
        var p = new Person { Name = "Ada", Address = new Address { Street = "old" } };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Address.Street)
                .Validate(v =>
                    string.IsNullOrEmpty(v) ? new[] { "street required" } : Array.Empty<string>()),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        // Swap to a fresh Address and re-render.
        p.Address = new Address { Street = "" };
        page.Render();

        await page.ChangeAsync("{\"value\":\"\"}");

        Assert.NotNull(captured);
        Assert.Contains("street required",
            captured!.GetValidationMessages(new FieldIdentifier(p.Address, nameof(Address.Street))));
    }

    [Fact]
    public async Task AsyncValidate_NestedField_TypingThenBlurSurfacesMessageInRenderedHtml()
    {
        // Mirrors the live showcase NestedAsyncWithLiveTotalsDemo exactly: an async Validate:
        // delegate on a nested string field that delays past a regex pre-check. The browser-side
        // flow is `input * N` (OnInput per keystroke, no validation because not yet touched) then
        // `change` once on blur (touches + validates async). The async validator awaits 300ms and
        // returns the undeliverable message; the post-handler re-render must include the message.
        // This is the same path the E2E test exercises — but in unit form so we can tell whether
        // the failure is render-pipeline or Server-WS-pipeline.
        var m = new StorefrontModel { Address = new StorefrontAddress { PostalCode = "" } };

        var page = RaskTest.Render(() => Form.Model(m)[
            Input.Bind(() => m.Address.PostalCode)
                .ValidateAsync(async (v, ct) =>
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        return new[] { "Postal code is required." };
                    }

                    if (!Regex.IsMatch(v, @"^\d{5}$"))
                    {
                        return new[] { "Postal code must be 5 digits." };
                    }

                    await Task.Delay(50, ct).ConfigureAwait(false);
                    return v == "99999" ? new[] { "We don't ship to this area." } : Array.Empty<string>();
                }),
            ValidationMessage.Template(msgs => [.. msgs.Select((s, i) => Div.Class("err").Key(i)[s])])
                .For(() => m.Address.PostalCode)
        ]);

        var initial = page.Render();
        var inputId = page.HandlerId("input");
        var changeId = page.HandlerId("change");

        // Simulate typing "99999" one character at a time via OnInput. None of these should
        // produce validation messages — the field isn't touched yet.
        foreach (var partial in new[] { "9", "99", "999", "9999", "99999" })
        {
            await page.InvokeAsync(inputId!, $"{{\"value\":\"{partial}\"}}");
        }

        Assert.DoesNotContain("don't ship", page.Render());

        // Blur with the final value — OnChange handler touches + runs async validator.
        await page.InvokeAsync(changeId!, "{\"value\":\"99999\"}");

        var afterBlur = page.Render();
        Assert.Contains("ship to this area", afterBlur);
    }

    [Fact]
    public async Task AsyncValidate_NestedField_TouchedKeystrokeAfterDeliveryError_ClearsMessage()
    {
        // Establishes the post-touch keystroke flow that the showcase relies on: after the async
        // validator's first message lands, typing a corrected value must clear the message via
        // OnInput re-validation. Same shape as the live demo "type 99999, see error; type 12345,
        // see error clear after async settles".
        var m = new StorefrontModel { Address = new StorefrontAddress { PostalCode = "" } };

        var page = RaskTest.Render(() => Form.Model(m)[
            Input.Bind(() => m.Address.PostalCode)
                .ValidateAsync(async (v, ct) =>
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        return new[] { "Postal code is required." };
                    }

                    if (!Regex.IsMatch(v, @"^\d{5}$"))
                    {
                        return new[] { "Postal code must be 5 digits." };
                    }

                    await Task.Delay(20, ct).ConfigureAwait(false);
                    return v == "99999" ? new[] { "We don't ship to this area." } : Array.Empty<string>();
                }),
            ValidationMessage.Template(msgs => [.. msgs.Select((s, i) => Div.Class("err").Key(i)[s])])
                .For(() => m.Address.PostalCode)
        ]);

        var initial = page.Render();
        var inputId = page.HandlerId("input");
        var changeId = page.HandlerId("change");

        // Step 1: blur with "99999" → undeliverable message lands.
        await page.InvokeAsync(inputId!, "{\"value\":\"99999\"}");

        await page.InvokeAsync(changeId!, "{\"value\":\"99999\"}");

        Assert.Contains("ship to this area", page.Render());

        // Step 2: now-touched field, type a valid value via OnInput. Async validator runs and
        // returns success; the message must clear.
        await page.InvokeAsync(inputId!, "{\"value\":\"12345\"}");

        Assert.DoesNotContain("ship to this area", page.Render());
    }

    [Fact]
    public async Task AsyncValidate_NestedField_SubmitAfterValidFill_RoutesToValidPath()
    {
        // Mirrors the showcase submit path: fill nested+root fields with valid values, then
        // simulate submit. The submit bridge calls ctx.TouchAllRegisteredFields() and
        // ctx.ValidateAsync() — every async per-field validator must run on the form's
        // EditContext (not a stray sub-object context) for OnValidSubmit to fire.
        var m = new StorefrontModel { CustomerName = "", Address = new StorefrontAddress { PostalCode = "" } };
        string? submitted = null;

        var page = RaskTest.Render(() => Form.Model(m).OnValidSubmit(mm => submitted = $"Charged to {mm.CustomerName}")[
            Input.Bind(() => m.CustomerName)
                .Validate(v => string.IsNullOrWhiteSpace(v) ? new[] { "Name required" } : Array.Empty<string>()),
            Input.Bind(() => m.Address.PostalCode)
                .ValidateAsync(async (v, ct) =>
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        return new[] { "Postal required" };
                    }

                    await Task.Delay(20, ct).ConfigureAwait(false);
                    return Array.Empty<string>();
                })
        ]);

        // Two inputs → two on-input handlers, in document order.
        var inputIds = page.HandlerIds("input");
        var nameInputId = inputIds[0];
        var postalInputId = inputIds[1];
        Assert.NotNull(postalInputId);

        await page.InvokeAsync(nameInputId!, "{\"value\":\"Ada\"}");

        await page.InvokeAsync(postalInputId!, "{\"value\":\"12345\"}");

        var submitId = page.HandlerId("submit");
        await page.InvokeAsync(submitId!, "{}");

        Assert.Equal("Charged to Ada", submitted);
    }

    [Fact]
    public async Task InlineValidate_NestedField_BlurSurfacesMessageInRenderedHtml()
    {
        // End-to-end coverage: after blur, the post-handler re-render must include the
        // validation message in the produced HTML. ContextCapture-style tests only prove the
        // form's EditContext has the message; this one proves ValidationMessage's read path
        // (EditContextScope.Current) actually resolves to the same context the handler wrote
        // to during the re-render that follows the event dispatch.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Address.Street)
                .Validate(v =>
                    string.IsNullOrEmpty(v) ? new[] { "street required" } : Array.Empty<string>()),
            ValidationMessage.Template(msgs => [.. msgs.Select((m, i) => Div.Class("err").Key(i)[m])])
                .For(() => p.Address.Street)
        ]);

        var initial = page.Render();
        Assert.DoesNotContain("street required", initial);

        var changeId = page.HandlerId("change");
        await page.InvokeAsync(changeId!, "{\"value\":\"\"}");

        var afterBlur = page.Render();
        Assert.Contains("street required", afterBlur);
    }

    [Fact]
    public async Task InlineValidate_NestedField_KeystrokeAfterBlurClearsMessageInRenderedHtml()
    {
        // After the first blur touches the field and produces a message, a keystroke that
        // makes the value valid must clear the message in the next render's HTML.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Address.Street)
                .Validate(v =>
                    v.Length < 3 ? new[] { "too short" } : Array.Empty<string>()),
            ValidationMessage.Template(msgs => [.. msgs.Select((m, i) => Div.Class("err").Key(i)[m])])
                .For(() => p.Address.Street)
        ]);

        var initial = page.Render();
        var changeId = page.HandlerId("change");
        await page.InvokeAsync(changeId!, "{\"value\":\"\"}");
        Assert.Contains("too short", page.Render());

        var inputId = page.HandlerId("input");
        await page.InvokeAsync(inputId!, "{\"value\":\"Oak\"}");

        var afterKeystroke = page.Render();
        Assert.DoesNotContain("too short", afterKeystroke);
    }

    [Fact]
    public async Task DeepChainBinding_TerminalOwner_ValidatesOnChange()
    {
        // The walker is BFS over public properties — confirm a two-hop chain still resolves.
        var p = new Person
        {
            Name = "Ada",
            Address = new Address { Street = "x", Postal = new PostalInfo { Code = "" } }
        };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form.Model(p)[
            Input.Bind(() => p.Address.Postal.Code)
                .Validate(v =>
                    string.IsNullOrEmpty(v) ? new[] { "postal required" } : Array.Empty<string>()),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        await page.ChangeAsync("{\"value\":\"\"}");

        Assert.NotNull(captured);
        Assert.Contains("postal required",
            captured!.GetValidationMessages(new FieldIdentifier(p.Address.Postal!, nameof(PostalInfo.Code))));
    }

    private sealed class StorefrontModel
    {
        public string CustomerName { get; set; } = "";
        public StorefrontAddress Address { get; set; } = new();
    }

    private sealed class StorefrontAddress
    {
        public string PostalCode { get; set; } = "";
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public Address Address { get; set; } = new();
    }

    private sealed class Address
    {
        public string Street { get; set; } = "";
        public PostalInfo? Postal { get; set; }
    }

    private sealed class PostalInfo
    {
        public string Code { get; set; } = "";
    }
}
