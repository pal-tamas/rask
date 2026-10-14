using System.Globalization;
using Microsoft.JSInterop;
using Rask.Core;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A handler that waits for something slow and then asks the browser several things one after the other,
///     saying after each how far it got — on the context it started on, or having left it as the kit's own do.
/// </summary>
public sealed partial class CallsAwaitedApp(IJSRuntime js, CallsAwaitedApp.Plan plan) : Component
{
    private string _said = "idle";

    protected override Component? HeadAssets => Markup.Title["calls awaited"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Button.OnClick(PickAsync)["pick"],
        Markup.P[_said]
    ];

    private async Task PickAsync()
    {
        _said = "waiting";
        await plan.Slow.Task.ConfigureAwait(plan.KeepsContext);
        for (var call = 1; call <= plan.Calls; call++)
        {
            await js.InvokeVoidAsync(Name(call)).ConfigureAwait(plan.KeepsContext);
            _said = "answered " + call.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The identifier of the <paramref name="call" />th call.</summary>
    public static string Name(int call) => "test.call" + call.ToString(CultureInfo.InvariantCulture);

    /// <summary>What the handler does, and the slow thing it waits for first.</summary>
    /// <param name="Calls">How many calls it awaits in a row.</param>
    /// <param name="KeepsContext">False for a handler that awaits with <c>ConfigureAwait(false)</c>.</param>
    public sealed record Plan(int Calls, bool KeepsContext)
    {
        /// <summary>Ended by the test once the page the handler's budget shows has gone out.</summary>
        public TaskCompletionSource Slow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
