using System.Globalization;
using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>One cell of the page: its own state and its own handler, as each chart on a page of charts has.</summary>
public sealed partial class EventBatchCell : Component
{
    private string _stamp = "-";

    public int Index { get; set; }

    protected override Component? Render() =>
        Div[Span[$"cell{Index}={_stamp};"], Button.Id($"hit{Index}").OnClick(Hit)["hit"]];

    // When this cell's handler ran, on a clock every cell reads: the order the handlers ran in.
    private void Hit() => _stamp = EventBatchApp.Tick();
}

/// <summary>
///     A page whose events can be told apart afterwards: sixty cells that each own a handler, and handlers of the
///     page itself — one that reads what the one before left, one that closes over what the last render computed,
///     one that awaits, one that navigates and one that throws.
/// </summary>
public sealed partial class EventBatchApp : Component
{
    public const int Cells = 60;

    private static int _clock;

    /// <summary>Completes the awaiting handler. Only the tests that press <c>slow</c> touch it.</summary>
    public static TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _walks;
    private int _count;
    private bool _busy;
    private string _log = "";

    internal static string Tick() => Interlocked.Increment(ref _clock).ToString(CultureInfo.InvariantCulture);

    protected override Component? HeadAssets => Markup.Title["event-batch"];
    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        // What this render computed, for the handler below to close over.
        var rendered = _count;
        return
        [
            Markup.P[$"walks={++_walks};count={_count};busy={_busy};log={_log};"],
            Button.Id("bump").OnClick(() => { _count++; _log += "b"; })["bump"],
            Button.Id("double").OnClick(() => { _count *= 2; _log += "d"; })["double"],
            Button.Id("snap").OnClick(() => _log += $"[{rendered}]")["snap"],
            Button.Id("slow").OnClick(SlowAsync)["slow"],
            Button.Id("go").OnClick(() => Go.To("/elsewhere"))["go"],
            Button.Id("boom").OnClick(() => throw new InvalidOperationException("boom in a batch"))["boom"],
            CellList()
        ];
    }

    private async Task SlowAsync()
    {
        _busy = true;
        await Gate.Task;
        _busy = false;
        _log += "s";
    }

    private static Component CellList()
    {
        var cells = new List<Component>();
        for (var i = 0; i < Cells; i++)
        {
            cells.Add(EventBatchCell.Index(i).Key(i));
        }

        return [.. cells.ToArray()];
    }
}
