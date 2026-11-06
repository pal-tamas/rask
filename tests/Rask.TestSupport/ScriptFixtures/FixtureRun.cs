using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Native.Promise;
using Jint.Runtime;

namespace Rask.TestSupport.ScriptFixtures;

/// <summary>
///     One fixture, start to finish, on its own Jint engine: the web globals installed, the bundle imported, then its
///     timers run on the virtual clock until it reports.
/// </summary>
/// <remarks>
///     What ends a run is the fixture's <c>report(...)</c>, the way <c>process.exit</c> ended one under Node: the task
///     that reports finishes, with its microtasks, and nothing after it runs. A run that empties its timers without
///     reporting, throws out of a task, leaves a rejection unhandled, or is still going after a virtual day fails —
///     each a reason a fixture's result would not mean what its test says.
/// </remarks>
internal sealed class FixtureRun
{
    private static readonly TimeSpan WallClockLimit = TimeSpan.FromSeconds(60);
    private const double VirtualTimeLimit = 24 * 60 * 60 * 1000;

    private readonly FixtureTimers _timers = new();
    private readonly FixtureOutcome _outcome = new();
    private readonly Engine _engine;

    public FixtureRun(IReadOnlyList<string> arguments)
    {
        var modules = new FixtureModuleLoader();
        _engine = new Engine(options =>
        {
            options.EnableModules(modules);
            options.TimeSystem = new FixtureTimeSystem(_timers, DateTimeOffset.UtcNow);
            options.TimeoutInterval(WallClockLimit);
        });
        _engine.Advanced.PromiseRejectionTracker += (_, e) =>
        {
            if (e.Operation == PromiseRejectionOperation.Reject)
            {
                _outcome.Rejected(e.Promise, e.Value ?? JsValue.Undefined);
            }
            else
            {
                _outcome.Handled(e.Promise);
            }
        };

        _engine.SetValue("__raskFixture", FixtureHost.Create(_engine, arguments, _outcome, _timers, modules));
        _engine.Execute(WebGlobals.Source, "rask-fixture-web-globals.js");
    }

    /// <summary>What the console printed, for a failure message to show beside its own reason.</summary>
    public string Console => _outcome.Console;

    /// <summary>Runs <paramref name="entry" /> and returns the JSON it reported, or why it reported none.</summary>
    public (string? Json, string? Failure) Run(string entry)
    {
        var deadline = DateTime.UtcNow + WallClockLimit;
        try
        {
            _engine.Execute(
                $"import({JsonSerializer.Serialize(entry)}).then(() => __raskFixture.settled(), (e) => __raskFixture.settled(e ?? 'undefined'));",
                "rask-fixture-entry.js");
            _engine.Advanced.ProcessTasks();
            while (_outcome.Report is null)
            {
                if (_outcome.Problem is { } problem)
                {
                    return (null, problem);
                }

                if (!_timers.TryTakeNext(out var callback))
                {
                    return (null, _outcome.EntrySettled
                        ? "The fixture ran to the end without calling report(...)."
                        : "The fixture is still awaiting something no timer will ever settle, and it never reported.");
                }

                if (_timers.Now > VirtualTimeLimit || DateTime.UtcNow > deadline)
                {
                    return (null, "The fixture was still running after a virtual day (or a minute of real time) — "
                        + "an interval that is never cleared, or a wait that never ends.");
                }

                _engine.Invoke(callback);
                _engine.Advanced.ProcessTasks();
            }

            return _outcome.Problem is { } late ? (null, late) : (_outcome.Report, null);
        }
        catch (JavaScriptException e)
        {
            return (null, "Uncaught " + FixtureOutcome.Describe(e.Error) + Environment.NewLine + e.JavaScriptStackTrace);
        }
    }
}
