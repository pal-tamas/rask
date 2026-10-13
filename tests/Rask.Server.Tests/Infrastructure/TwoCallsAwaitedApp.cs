using Microsoft.JSInterop;
using Rask.Core;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A handler written as the kit writes its own: it waits for something slow, then asks the browser two things
///     one after the other, never coming back through the context it started on.
/// </summary>
public sealed partial class TwoCallsAwaitedApp(IJSRuntime js, TwoCallsAwaitedApp.Slow slow) : Component
{
    private string _said = "idle";

    protected override Component? HeadAssets => Markup.Title["two calls awaited"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Button.OnClick(PickAsync)["pick"],
        Markup.P[_said]
    ];

    private async Task PickAsync()
    {
        _said = "waiting";
        await slow.Done.Task.ConfigureAwait(false);
        await js.InvokeVoidAsync("test.write").ConfigureAwait(false);
        _said = "written";
        await js.InvokeVoidAsync("test.focus").ConfigureAwait(false);
        _said = "focused";
    }

    /// <summary>What the handler waits for first, ended by the test once the handler's page has gone out.</summary>
    public sealed class Slow
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
