using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// End-to-end regression coverage for the WS handler-dispatch FIFO contract.
// The framework dispatches inbound WS messages by chaining each handler's
// task onto a per-session `LastHandlerTask`, so a later-arriving message
// cannot start dispatching before an earlier one. The prior implementation
// fire-and-forgot each dispatch via `Task.Run` and trusted SemaphoreSlim's
// FIFO contract to preserve order — but SemaphoreSlim is FIFO on WaitAsync
// invocation order, not on Task.Run creation order, so the ThreadPool could
// reorder dispatches and let a submit handler read a stale model that the
// preceding input handler hadn't yet applied.
//
// The unit-test counterpart lives at
// Rask.Server.Tests/WebSockets/HandlerOrderingTests.cs and exercises the
// contract under explicit ThreadPool contention. This file pins the
// user-visible symptom: rapid FillAsync → ClickAsync(submit) without any
// intermediate wait must always activate the form.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task HandlerOrdering_RapidFillThenSubmit_OnValidSubmitFires() => RunAsync(async () =>
    {
        // FirstErrorWinsDemo's submit handler reads `_model.Code` and writes
        // `_submission = $"Activated: {m.Code}"`. The input that sets Code
        // streams via data-rask-on-input. If the WS dispatcher serialised the
        // input message AFTER the submit message (the pre-fix bug), submit
        // would see an empty Code, validation would fail, and the success
        // banner would never appear.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var form = Page.Locator("form:has(#v8-code)");
        var field = form.Locator("#v8-code");

        // No intermediate wait between Fill and Click — exactly the shape
        // that exposed the race. With FIFO chaining the server sees the
        // input update before the submit and OnValidSubmit fires.
        await field.FillAsync("ABC-123");
        await form.Locator("button[type=submit]").ClickAsync();

        await Expect(Page.Locator(".sample-result-body:has(#v8-code) .alert-success"))
            .ToContainTextAsync("Activated: ABC-123",
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });
}
