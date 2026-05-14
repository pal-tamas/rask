using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Forms / binding / validation suite. Every test in this class runs across every host
// collection that inherits the class (Server, Wasm.Host, Standalone WASM) so that any
// observable divergence on a forms surface fails the build on the diverging host.
//
// Path navigation goes through NavigateToAsync. The default implementation just calls
// Page.GotoAsync(path), which works for the ASP.NET hosts that install a SPA fallback.
// StandaloneWasmExampleTests overrides it to home-then-sidebar because WasmAppHost has no
// SPA fallback (deep links 404).
public abstract class SharedSmokeTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;

    protected IPage Page = default!;

    protected SharedSmokeTests(PlaywrightFixture pw) => _pw = pw;

    protected abstract string BaseUrl { get; }
    protected abstract string FixtureName { get; }
    protected abstract string ServerLog { get; }

    public async Task InitializeAsync()
    {
        _ctx = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });
        Page = await _ctx.NewPageAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    // Default = direct deep link. Overridden by hosts (e.g. WasmAppHost) that don't install
    // a SPA fallback; those must navigate via the home shell + sidebar instead.
    protected virtual Task NavigateToAsync(string path) => Page.GotoAsync(path);

    protected Task ClickSidebar(string label) =>
        Page.Locator("aside.side-nav button.nav-item-btn:has-text(\"" + label + "\")").ClickAsync();

    protected async Task RunAsync(Func<Task> body, [CallerMemberName] string testName = "")
    {
        try
        {
            await body();
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName, testName, ServerLog);
        }
    }

    // ---------- Binding ----------

    [Fact]
    public Task Binding_TypedBindUpdatesEcho() => RunAsync(async () =>
    {
        await NavigateToAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The Bind helper derives the input's name attribute from the expression's
        // property — `() => _model.Name` produces <input name="Name">, which the
        // manual Value+OnInput section does not emit, so the locator is unique.
        var bound = Page.Locator("input[name=Name]").First;
        await bound.FillAsync("Ada");

        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "Hello," }))
            .ToContainTextAsync("Ada", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Binding_StartDateFirstChange_UpdatesEchoAndInputValue() => RunAsync(async () =>
    {
        // Regression: <input type="date"> bound to DateOnly is change-only (no
        // data-rask-on-input). The morph used to guard from.value/from.checked
        // sync with `document.activeElement !== from` — fine for streaming text
        // inputs, but a problem for change-only inputs where the server's
        // rendered value is authoritative.  Simulate the date-picker flow by
        // focusing the input, writing the value, and dispatching `change` from
        // JS while focus is held; assert both the echo and the input's own
        // .value end up in sync after the round trip.
        await NavigateToAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var startDate = Page.Locator("#bind-start");
        await Expect(startDate).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Drive a date-picker-like change with focus held. `await` returns once
        // the async dispatch round-trips so the next assertion sees post-morph state.
        await Page.EvaluateAsync(@"async () => {
            const el = document.getElementById('bind-start');
            el.focus();
            el.value = '2026-05-15';
            el.dispatchEvent(new Event('change', { bubbles: true }));
        }");

        // "Subscribe =" only appears in the echo, never in the syntax-highlighted
        // source sample, so this disambiguates the two <pre><code> blocks.
        var echo = Page.Locator("pre code").Filter(
            new LocatorFilterOptions { HasText = "Subscribe =" });
        await Expect(echo).ToContainTextAsync("StartDate = 2026-05-15",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Input's own .value must agree with the echo — this is what the morph
        // focus-guard fix protects.
        await Expect(startDate).ToHaveValueAsync("2026-05-15",
            new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        // Second change while focus is still on the input — locks the regression
        // and proves the fix isn't order-dependent.
        await Page.EvaluateAsync(@"async () => {
            const el = document.getElementById('bind-start');
            el.focus();
            el.value = '2027-02-03';
            el.dispatchEvent(new Event('change', { bubbles: true }));
        }");
        await Expect(echo).ToContainTextAsync("StartDate = 2027-02-03",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(startDate).ToHaveValueAsync("2027-02-03",
            new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Binding_Checkbox_TogglesBoolModelTwice() => RunAsync(async () =>
    {
        // Proves Input.Bound<bool> wires BoolToggleHandler on OnChangeAsync: each click
        // immediately notifies changed+touched and pushes the negated value through the
        // round trip. Two flips → the echo must show "true" then "false" again.
        await NavigateToAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var checkbox = Page.Locator("#bind-subscribe");
        var echo = Page.Locator("pre code").Filter(
            new LocatorFilterOptions { HasText = "Subscribe =" });

        await Expect(echo).ToContainTextAsync("Subscribe = false",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await checkbox.ClickAsync();
        await Expect(echo).ToContainTextAsync("Subscribe = true",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await checkbox.ClickAsync();
        await Expect(echo).ToContainTextAsync("Subscribe = false",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Binding_NumberInput_UpdatesIntEcho() => RunAsync(async () =>
    {
        // <input type=number> is change-only. Same morph-reset discipline as the
        // validation tests: fill, fire change via Tab, and wait for the rendered .value
        // to commit before asserting the echo.
        await NavigateToAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var age = Page.Locator("#bind-age");
        await age.FillAsync("42");
        await age.PressAsync("Tab");

        await Expect(age).ToHaveValueAsync("42",
            new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        var echo = Page.Locator("pre code").Filter(
            new LocatorFilterOptions { HasText = "Subscribe =" });
        await Expect(echo).ToContainTextAsync("Age       = 42",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Binding_EnumSelect_UpdatesEcho() => RunAsync(async () =>
    {
        // Select.Bound on an enum-typed property: the option's value attribute is the
        // enum member name, RouteValueParser converts it back through BindingHelpers
        // .TrySetTyped, and the echo reflects the new enum value.
        await NavigateToAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#bind-favorite").SelectOptionAsync("Red");

        var echo = Page.Locator("pre code").Filter(
            new LocatorFilterOptions { HasText = "Subscribe =" });
        await Expect(echo).ToContainTextAsync("Favorite  = Red",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Binding_Textarea_StreamsEachKeystroke() => RunAsync(async () =>
    {
        // Textarea.Bound wires OnInputAsync for every keystroke (textareas are inherently
        // string-valued, no "deferred" mode). Fill mutates .value and dispatches input —
        // the echo must update without any blur, change, or submit.
        await NavigateToAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var textarea = Page.Locator("#bind-textarea");
        await Expect(textarea).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await textarea.FillAsync("hello rask");

        // Scope to the live-result column so the assertion can't lock onto the syntax-
        // highlighted source sample in the same CodeSample card (both contain "Notes =").
        var echo = Page.Locator(".sample-result-body:has(#bind-textarea) pre code");
        await Expect(echo).ToContainTextAsync("Notes  = \"hello rask\"",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(echo).ToContainTextAsync("Length = 10",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // ---------- Validation: DataAnnotations (per-field) ----------

    [Fact]
    public Task Validation_InvalidSubmit_ShowsRequiredMessages() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The per-field demo's form is the one containing #v1-name.
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();

        await Expect(Page.Locator("form:has(#v1-name) .text-danger").First)
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
    });

    [Fact]
    public Task Validation_ValidSubmit_RendersSuccessBanner() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Bind Age first to avoid the change-only input race: a name/email OnInput re-
        // render's morph would otherwise reset Age's .value back to the server's still-
        // zero model value, and the resulting blur would have no value-change to fire
        // `change` on. See the longer comment in
        // Validation_Summary_EmptySubmit_RendersHeadlessTemplate.
        await Page.Locator("#v1-age").FillAsync("28");
        await Page.Locator("#v1-age").PressAsync("Tab");
        await Expect(Page.Locator("#v1-age"))
            .ToHaveValueAsync("28", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        await Page.Locator("#v1-plan").SelectOptionAsync("pro");
        await Page.Locator("#v1-name").FillAsync("Ada Lovelace");
        await Page.Locator("#v1-email").FillAsync("ada@example.com");
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();

        await Expect(Page.Locator(".alert-success").First)
            .ToContainTextAsync("Ada Lovelace",
                new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
    });

    [Fact]
    public Task Validation_FixingInvalidField_HidesItsMessage() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // 1. Submit empty → every field reports an error.
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();
        var nameField = Page.Locator("form:has(#v1-name) div:has(> #v1-name)");
        await Expect(nameField.Locator(".text-danger"))
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });

        // 2. Fill a valid value into Name.
        await Page.Locator("#v1-name").FillAsync("Ada Lovelace");

        // 3. The Name field's error message must disappear once it becomes valid,
        //    even though focus hasn't left the input (only the input event has fired).
        await Expect(nameField.Locator(".text-danger"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // 4. Other fields still report their own errors — only Name cleared.
        await Expect(Page.Locator("form:has(#v1-name) div:has(> #v1-email) .text-danger"))
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000, IgnoreCase = true });
    });

    [Fact]
    public Task Validation_Range_BelowFloor_ShowsRangeMessage() => RunAsync(async () =>
    {
        // [Range(13, 120)] on RegistrationModel.Age — filling "5" must surface the
        // attribute's distinct message ("Age must be between 13 and 120.") on the Age
        // field, not the generic "required" path. Morph-reset discipline as elsewhere.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#v1-age").FillAsync("5");
        await Page.Locator("#v1-age").PressAsync("Tab");
        await Expect(Page.Locator("#v1-age"))
            .ToHaveValueAsync("5", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();

        var ageField = Page.Locator("form:has(#v1-name) div:has(> #v1-age)");
        await Expect(ageField.Locator(".text-danger"))
            .ToContainTextAsync("between 13 and 120",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_StringLength_TooLong_ShowsStringLengthMessage() => RunAsync(async () =>
    {
        // [StringLength(40, MinimumLength = 2)] on RegistrationModel.Name — pipe in 41
        // characters and assert the attribute's specific message surfaces on Name.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#v1-name").FillAsync(new string('x', 41));
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();

        var nameField = Page.Locator("form:has(#v1-name) div:has(> #v1-name)");
        await Expect(nameField.Locator(".text-danger"))
            .ToContainTextAsync("2–40 characters",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_EmailAddress_BadFormat_ShowsEmailMessage() => RunAsync(async () =>
    {
        // [EmailAddress] on RegistrationModel.Email — "not-an-email" must surface the
        // attribute's specific message, not "required" (the field has content).
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#v1-email").FillAsync("not-an-email");
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();

        var emailField = Page.Locator("form:has(#v1-name) div:has(> #v1-email)");
        await Expect(emailField.Locator(".text-danger"))
            .ToContainTextAsync("valid email",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // ---------- Validation: ValidationSummary ----------

    [Fact]
    public Task Validation_Summary_EmptySubmit_RendersHeadlessTemplate() => RunAsync(async () =>
    {
        // The summary demo wraps the headless ValidationSummary with the user-defined
        // SummaryAlert template: a Bootstrap alert containing a <strong>{Field}</strong>
        // per ValidationEntry. Submitting empty must invoke the template (proving the
        // EditContext lookup, post-handler re-render, and Template delegate all line up).
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v2-name)");
        await form.Locator("button[type=submit]").ClickAsync();

        var alert = form.Locator(".alert-danger").First;
        await Expect(alert).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(alert).ToContainTextAsync("Name is required",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(alert.Locator("li").Filter(new LocatorFilterOptions { HasText = "Name" })
                .Locator("strong").First)
            .ToHaveTextAsync("Name");
        await Expect(alert.Locator("li").Filter(new LocatorFilterOptions { HasText = "Email" })
                .Locator("strong").First)
            .ToHaveTextAsync("Email");

        // Filling every field and resubmitting must clear the summary entirely —
        // ValidationSummary returns Fragment() when GetValidationEntries is empty,
        // so the .alert-danger DOM node must disappear, not merely be hidden.
        //
        // Order matters under load. <input type=number> is change-only (no data-rask-
        // on-input), and rask.js's morph FORCES the rendered server value into the
        // input's .value on every re-render for change-only inputs (see rask.js morph
        // for INPUT — "change-only inputs are server-authoritative on every commit").
        // Form.BuildSubmitBridge validates ctx.Model, which is built from per-field
        // change events — NOT the submit FormData — so if Age never fires `change`,
        // ctx.Model.Age stays at 0 and Range[13,120] rejects the submit.
        //
        // The race: if Age is filled BEFORE the OnInput re-renders from Name/Email
        // round-trip, those subsequent morphs reset Age's .value back to "0" (the
        // server's still-zero model value). The browser then sees focus-time value 0
        // and current value 0 on blur, so it suppresses the `change` event entirely
        // and Age never reaches the server.
        //
        // Fix: bind Age first, fire its change via a real Tab keypress (programmatic
        // element.blur() is unreliable on number inputs in headless Chromium under
        // load), and wait for the round-trip — proven by the rendered value attribute
        // catching up — before touching any other field.
        await form.Locator("#v2-age").FillAsync("28");
        await form.Locator("#v2-age").PressAsync("Tab");
        await Expect(form.Locator("#v2-age"))
            .ToHaveValueAsync("28", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        await form.Locator("#v2-plan").SelectOptionAsync("pro");
        await form.Locator("#v2-name").FillAsync("Ada Lovelace");
        await form.Locator("#v2-email").FillAsync("ada@example.com");
        await form.Locator("button[type=submit]").ClickAsync();

        // Success banner first — proves OnValidSubmit fired (and therefore the post-
        // handler render observed an empty EditContext). Scope to the v2 demo container
        // so the locator can't lock onto an alert from a different demo on the page, and
        // give the submit round-trip more headroom: this turn flushes five queued WS
        // messages (name OnInput × 1, email OnInput × 1, age OnChange, plan OnChange,
        // submit) before the success render arrives, which can briefly exceed 10 s under
        // parallel-class CI load.
        var demo = Page.Locator(".sample-result-body:has(#v2-name)");
        await Expect(demo.Locator(".alert-success"))
            .ToContainTextAsync("Ada Lovelace",
                new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        // Same render must have collapsed the headless summary: GetValidationEntries() is
        // empty, ValidationSummary returns Fragment(), the previous template DOM disappears.
        await Expect(form.Locator(".alert-danger")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    });

    // ---------- Validation: async (UniqueUsernameValidator) ----------

    [Fact]
    public Task Validation_AsyncDemo_ShowsCheckingThenTakenMessage() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v3-username)");
        await form.Locator("#v3-username").FillAsync("admin");

        // Blur to fire OnChange → marks the field touched → triggers async ValidateFieldAsync.
        // The validator delays 400ms; the .validating-indicator span must surface during that
        // window. Pin on the framework class (ValidatingIndicator.cs:87), not Bootstrap utilities,
        // so the assertion fails loudly if the pending render is missing on either host.
        await form.Locator("#v3-username").BlurAsync();

        await Expect(form.Locator(".validating-indicator"))
            .ToContainTextAsync("Checking",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000, IgnoreCase = true });

        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("taken",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });

        // After the validator completes the indicator must collapse back to an empty fragment.
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Validation_AsyncDemo_ValidUsername_ReachesSuccessBanner() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The success banner is a sibling of the form (it lives inside the same
        // sample-result-body wrapper alongside the form), so scope the alert lookup to that
        // demo container rather than the form itself.
        var demo = Page.Locator(".sample-result-body:has(#v3-username)");
        var form = demo.Locator("form");
        await form.Locator("#v3-username").FillAsync("ada-lovelace");

        // Let the per-field async validator finish (indicator clears) before submitting, so the
        // submit-side ValidateAsync doesn't race the in-flight per-field run.
        await form.Locator("#v3-username").BlurAsync();
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(demo.Locator(".alert-success"))
            .ToContainTextAsync("ada-lovelace",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_AsyncDemo_RetypingAfterTaken_ClearsMessage() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v3-username)");
        var field = form.Locator("#v3-username");

        await field.FillAsync("admin");
        await field.BlurAsync();
        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("taken",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });

        // Correct the value. For strings the OnInput handler validates as soon as the field is
        // touched (BindingHelpers.StringSetHandler line 88), so the message must clear without
        // another blur.
        await field.FillAsync("ada-lovelace");
        await Expect(form.Locator(".text-danger"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Validation_AsyncDemo_LatestWinsCancellation_OnlyFinalResultShown() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v3-username)");
        var field = form.Locator("#v3-username");

        // Touch the field first so subsequent OnInput keystrokes trigger validation.
        await field.FillAsync("seed");
        await field.BlurAsync();
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // Fire "admin" (would emit "taken" after 400ms), then replace before that window expires.
        // Latest-wins must cancel the in-flight run so no "taken" message appears.
        await field.FillAsync("admin");
        await Page.WaitForTimeoutAsync(50);
        await field.FillAsync("ada-lovelace");

        // Indicator settles, no stale "taken" message remains.
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Expect(form.Locator(".text-danger"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Validation_AsyncDemo_SubmitWhilePending_AwaitsValidation() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Success banner is a form-sibling inside the same sample-result-body, so scope the
        // banner negation to the demo container.
        var demo = Page.Locator(".sample-result-body:has(#v3-username)");
        var form = demo.Locator("form");
        var field = form.Locator("#v3-username");

        await field.FillAsync("admin");
        // Click submit immediately. Form.BuildSubmitBridge awaits ctx.ValidateAsync() which
        // cancels any per-field in-flight run and reruns sync + async validators from scratch.
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("taken",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
        await Expect(demo.Locator(".alert-success"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 2_000 });
    });

    [Fact]
    public Task Validation_AsyncDemo_EmptyInput_ShowsDataAnnotationsRequired() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v3-username)");

        // Submit empty. UniqueUsernameValidator.CheckAsync short-circuits on empty input,
        // so only the DataAnnotations [Required] check runs through the submit bridge's
        // ValidateAsync. Blur-on-empty wouldn't fire `change` (the value didn't actually
        // change), so going through submit is the deterministic trigger.
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000, IgnoreCase = true });
        // The async path never produced a pending state, so the indicator must never appear.
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 2_000 });
        // Success banner must NOT appear — the form did not pass validation. The banner lives
        // outside the <form> in the demo's sample-result-body, so scope to that wrapper.
        await Expect(Page.Locator(".sample-result-body:has(#v3-username) .alert-success"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 2_000 });
    });

    [Fact]
    public Task Validation_AsyncDemo_ExceptionInValidator_ShowsGenericMessage() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v3-username)");
        var field = form.Locator("#v3-username");

        // The literal "explode" forces UniqueUsernameValidator.CheckAsync to throw mid-await.
        // EditContext.ValidateFieldAsync catches the exception and pushes the framework's generic
        // "Validation could not be completed." message via AddValidationMessage.
        await field.FillAsync("explode");
        await field.BlurAsync();

        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("could not be completed",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
    });

    [Fact]
    public Task Validation_AsyncDemo_RapidTyping_NoLingeringIndicator() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v3-username)");
        var field = form.Locator("#v3-username");

        // Touch the field so OnInput keystrokes validate.
        await field.FillAsync("seed");
        await field.BlurAsync();
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        await field.FillAsync("");
        await field.PressSequentiallyAsync("xylophone",
            new LocatorPressSequentiallyOptions { Delay = 60 });

        // The final value "xylophone" doesn't match any Taken entry, so once everything settles
        // there must be no lingering indicator and no taken/required messages.
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Expect(form.Locator(".text-danger"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    // ---------- Validation: inline Validate (field + form) ----------

    [Fact]
    public Task Validation_InlineFieldValidate_ShowsErrorAfterTouch() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The InlineValidateDemo's form holds #v4-email with an inline Validate: callback.
        var form = Page.Locator("form:has(#v4-email)");
        var email = form.Locator("#v4-email");

        // Type an obviously-invalid email and blur to touch the field. The inline Validate
        // delegate on the Input (Func<string, IEnumerable<string>>) flags missing '@'.
        await email.FillAsync("not-an-email");
        await email.BlurAsync();

        await Expect(form.Locator("#v4-email + .text-danger, #v4-email ~ .text-danger").First)
            .ToContainTextAsync("Email looks wrong",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });

        // Fixing the value (string fields revalidate per-keystroke once touched) clears the
        // inline rule's message.
        await email.FillAsync("ada@example.com");
        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Email looks wrong" }))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Validation_InlineFieldValidate_FiresOnSubmit_WithoutPriorTouch() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Submit the InlineValidateDemo without typing into the email field. The inline
        // Validate delegate rejects any value that doesn't contain '@', so an empty value
        // must surface "Email looks wrong" once submit drives ctx.ValidateAsync past every
        // registered field delegate (touched or not).
        var form = Page.Locator("form:has(#v4-email)");
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(form.Locator("#v4-email + .text-danger, #v4-email ~ .text-danger").First)
            .ToContainTextAsync("Email looks wrong",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Validation_InlineFormValidate_AddsSummaryErrorOnMismatch() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v4-email)");

        // Form-level Validate: callback returns ["Passwords do not match."] when
        // password != confirm. Trigger it by submitting with a mismatch.
        await form.Locator("#v4-email").FillAsync("ada@example.com");
        await form.Locator("#v4-password").FillAsync("alpha");
        await form.Locator("#v4-confirm").FillAsync("bravo");
        await form.Locator("button[type=submit]").ClickAsync();

        // The summary alert in InlineValidateDemo only renders form-level messages, so
        // this assertion specifically validates the Validate: form-level path.
        await Expect(form.Locator(".alert-danger"))
            .ToContainTextAsync("Passwords do not match",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Validation_InlineFieldAsyncValidate_ShowsRemoteError() => RunAsync(async () =>
    {
        // Drives the typed async Validate overload (Func<T, CT, ValueTask<IEnumerable<string>>>)
        // on the Input. CheckCodeAsync sleeps 250ms then flags reserved codes — the test types
        // a reserved code, blurs, and waits for the async result to land in .text-danger.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v10-code)");
        var field = form.Locator("#v10-code");

        await field.FillAsync("BAD-001");
        await field.BlurAsync();

        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("reserved",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
    });

    [Fact]
    public Task Validation_InlineFormAsyncValidate_AddsSummaryErrorAtSubmit() => RunAsync(async () =>
    {
        // Drives the typed async Validate overload on Form<PromoModel>. With an empty Code
        // the async form-level rule returns ["Code is required."], which lands as a form-
        // level entry rendered by the SummaryAlert (.alert-danger).
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v10-code)");
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(form.Locator(".alert-danger"))
            .ToContainTextAsync("Code is required",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // ---------- Validation: new demos (v5 cross-field, v6 programmatic, v7 FluentValidation) ----------

    [Fact]
    public Task Validation_CrossFieldSummary_AddsFormLevelMessageOnMismatch() => RunAsync(async () =>
    {
        // Form<TripModel>.Validate returns ["Return date must be after departure."] when
        // Return ≤ Depart. The CrossFieldSummaryDemo's ValidationSummary renders that as a
        // bare <li> (no Field strong tag), so this test asserts the form-level path lands
        // in the summary with the exact rule message.
        //
        // Date inputs are change-only — same morph-reset discipline as #v1-age: bind
        // Depart first, wait for it to commit, then bind Return.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v5-depart)");

        // Make Depart later than Return → cross-field rule rejects.
        await form.Locator("#v5-depart").FillAsync("2026-07-15");
        await form.Locator("#v5-depart").PressAsync("Tab");
        await Expect(form.Locator("#v5-depart"))
            .ToHaveValueAsync("2026-07-15", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        await form.Locator("#v5-return").FillAsync("2026-06-01");
        await form.Locator("#v5-return").PressAsync("Tab");
        await Expect(form.Locator("#v5-return"))
            .ToHaveValueAsync("2026-06-01", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(form.Locator(".alert-danger"))
            .ToContainTextAsync("Return date must be after departure",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_ProgrammaticValidate_RaisesMessagesWithoutSubmit() => RunAsync(async () =>
    {
        // Click "#v6-validate-now" with an empty Title — that button calls
        // EditContext.ValidateAsync() directly, not through Form.BuildSubmitBridge. The
        // [Required] DataAnnotation hangs on TaskModel.Title via the form's resolved
        // EditContext, but no DataAnnotationsValidator is in the tree for v6 — instead
        // the test asserts the SlowTitleValidator path is exercised (via "duplicate"
        // below). Here we just assert: nothing about submit changes; no success banner.
        //
        // Wait — TaskModel has [Required] but ValidationPage's v6 form has no
        // DataAnnotationsValidator, so [Required] doesn't surface there. We assert the
        // SlowTitleValidator-driven behavior instead by filling a passing value first.
        // Strict assertion: clicking Validate now with content "duplicate" raises the
        // duplicate message without firing OnValidSubmit/OnInvalidSubmit (no banner).
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var demo = Page.Locator(".sample-result-body:has(#v6-title)");
        var form = demo.Locator("form");

        await form.Locator("#v6-title").FillAsync("duplicate");
        await form.Locator("#v6-title").BlurAsync();
        // Let any per-field validation that fires from blur finish before the programmatic
        // run kicks off so the assertion below only observes the "Validate now" outcome.
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        await form.Locator("#v6-validate-now").ClickAsync();

        // The validator's 600ms delay completes and pushes the duplicate message into the
        // ValidationMessage template — proves Validate() outside submit reaches the same
        // EditContext as field bindings.
        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("already used",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Critically: no success banner — OnValidSubmit must NOT have fired.
        await Expect(demo.Locator(".alert-success"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 2_000 });
    });

    [Fact]
    public Task Validation_SubmitDisabledWhileValidating() => RunAsync(async () =>
    {
        // SlowTitleValidator delays 600ms. The submit button binds Disabled to
        // EditContext.IsValidatingAny, so during that window the button must report
        // disabled, and once the validator settles the disabled attribute must drop.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v6-title)");
        var submit = form.Locator("#v6-submit");

        await form.Locator("#v6-title").FillAsync("any-title");
        await form.Locator("#v6-title").BlurAsync();

        // Within the 600ms window the button is disabled — assert it observably reaches
        // that state. Playwright's IsDisabledAsync only reads the current snapshot, so use
        // the assertion form that polls until the condition holds.
        await Expect(submit).ToBeDisabledAsync(
            new LocatorAssertionsToBeDisabledOptions { Timeout = 5_000 });

        // After the validator completes, the next re-render flips the prop back. Empty
        // title is allowed by SlowTitleValidator (CheckAsync returns early on whitespace),
        // so for non-empty + non-duplicate the field validates clean.
        await Expect(submit).Not.ToBeDisabledAsync(
            new LocatorAssertionsToBeDisabledOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_FluentValidation_QuantityRule_Surfaces() => RunAsync(async () =>
    {
        // OrderValidator.RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1) — leave Quantity at
        // 0 and Product empty, submit, assert Fluent's message appears in the field-level
        // .text-danger. Proves FluentValidationValidator's Inner.ValidateAsync path on
        // submit (the form's OnSubmit bridges via Form.BuildSubmitBridge).
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v7-quantity)");
        await form.Locator("button[type=submit]").ClickAsync();

        // Two .text-danger divs render (one per field), so filter each by message text
        // before asserting — strict mode forbids multi-match on a bare locator.
        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Quantity must be at least 1" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Product is required" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_FirstErrorWins_InlineGatesDataAnnotations_ThenFlips() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // FirstErrorWinsDemo: inline rule rejects whitespace ("Code is required."), and a
        // [RegularExpression] DA rule rejects any non "ABC-123" value. EditContext gates the
        // DA rule while the inline rule is failing, so an empty submit must only surface the
        // inline message — the DA "Use the ABC-123 format." must NOT also appear.
        var form = Page.Locator("form:has(#v8-code)");
        var field = form.Locator("#v8-code");

        await form.Locator("button[type=submit]").ClickAsync();

        var errors = form.Locator(".text-danger");
        await Expect(errors).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Expect(errors.First).ToContainTextAsync("Code is required",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });

        // Type a value that satisfies the inline rule (non-empty) but fails DA. With the
        // inline rule clean, gating releases and the DA message flips in.
        await field.FillAsync("abc");
        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("ABC-123 format",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });

        // Fix the DA rule too — both stages now pass and the success banner lands.
        await field.FillAsync("ABC-123");
        await form.Locator("button[type=submit]").ClickAsync();
        await Expect(Page.Locator(".sample-result-body:has(#v8-code) .alert-success"))
            .ToContainTextAsync("Activated: ABC-123",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_FluentValidationAsync_ChainTransitionsToMustAsync() => RunAsync(async () =>
    {
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // FluentValidationAsyncDemo: a single RuleFor with CascadeMode.Stop ending in MustAsync.
        // Empty submit hits NotEmpty; "abc" hits Matches; "TKT-001" hits MustAsync after the
        // 400ms await (indicator visible during the wait); "TKT-999" submits successfully.
        var form = Page.Locator("form:has(#v9-code)");
        var field = form.Locator("#v9-code");

        await form.Locator("button[type=submit]").ClickAsync();
        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("Code is required",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });

        await field.FillAsync("abc");
        await field.BlurAsync();
        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("TKT-123",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });

        // Reserved code → after the 400ms MustAsync await, the message lands. The indicator
        // surfaces during the await window.
        await field.FillAsync("TKT-001");
        await field.BlurAsync();
        await Expect(form.Locator(".validating-indicator"))
            .ToContainTextAsync("Checking",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000, IgnoreCase = true });
        await Expect(form.Locator(".text-danger"))
            .ToContainTextAsync("already reserved",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // Free code → submit succeeds.
        await field.FillAsync("TKT-999");
        await field.BlurAsync();
        await Expect(form.Locator(".validating-indicator"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await form.Locator("button[type=submit]").ClickAsync();
        await Expect(Page.Locator(".sample-result-body:has(#v9-code) .alert-success"))
            .ToContainTextAsync("Reserved: TKT-999",
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task Validation_ValidatableObject_AttributeAndInterfaceErrors_SurfaceTogether() => RunAsync(async () =>
    {
        // ASP.NET Core parity assertion. BookingModel pairs [Required] on Name with an
        // IValidatableObject.Validate that returns BOTH a per-field result for Departure
        // (MemberNames=["Departure"]) and a form-level result (empty MemberNames). The BCL's
        // TryValidateObject would silence Validate() the moment [Required] fails — Rask's
        // DataAnnotationsValidator invokes the interface directly, so all three errors must
        // surface in the same submit cycle. The test then fixes each error in turn and asserts
        // it disappears independently before submitting cleanly to the success banner.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var demo = Page.Locator(".sample-result-body:has(#v11-name)");
        var form = demo.Locator("form");

        // Date inputs are change-only — match the discipline used by CrossFieldSummaryDemo's e2e:
        // fill, blur, wait for the committed value before touching the next one.
        await form.Locator("#v11-departure").FillAsync("2020-01-01");
        await form.Locator("#v11-departure").PressAsync("Tab");
        await Expect(form.Locator("#v11-departure"))
            .ToHaveValueAsync("2020-01-01", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        await form.Locator("#v11-arrival").FillAsync("2019-12-31");
        await form.Locator("#v11-arrival").PressAsync("Tab");
        await Expect(form.Locator("#v11-arrival"))
            .ToHaveValueAsync("2019-12-31", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        await form.Locator("button[type=submit]").ClickAsync();

        // Attribute error on Name (per-field ValidationMessage template).
        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Name is required" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // IValidatableObject per-field result for Departure.
        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Departure cannot be in the past" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // IValidatableObject form-level result lands in the summary.
        await Expect(form.Locator(".alert-danger"))
            .ToContainTextAsync("Arrival must be after departure",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Fix Name only — its [Required] error clears; the two IValidatableObject errors stay.
        await form.Locator("#v11-name").FillAsync("Ada");
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Name is required" }))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Departure cannot be in the past" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(form.Locator(".alert-danger"))
            .ToContainTextAsync("Arrival must be after departure",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Fix Departure → past-date error clears; form-level error still holds because
        // Arrival (2019-12-31) is still ≤ Departure (2026-07-01).
        await form.Locator("#v11-departure").FillAsync("2026-07-01");
        await form.Locator("#v11-departure").PressAsync("Tab");
        await Expect(form.Locator("#v11-departure"))
            .ToHaveValueAsync("2026-07-01", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(form.Locator(".text-danger").Filter(
                new LocatorFilterOptions { HasText = "Departure cannot be in the past" }))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(form.Locator(".alert-danger"))
            .ToContainTextAsync("Arrival must be after departure",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Fix Arrival → all errors clear, success banner lands.
        await form.Locator("#v11-arrival").FillAsync("2026-07-05");
        await form.Locator("#v11-arrival").PressAsync("Tab");
        await Expect(form.Locator("#v11-arrival"))
            .ToHaveValueAsync("2026-07-05", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(demo.Locator(".alert-success"))
            .ToContainTextAsync("Booked: Ada 2026-07-01",
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });
}
