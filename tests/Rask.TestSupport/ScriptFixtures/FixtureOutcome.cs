using System.Text;
using Jint;
using Jint.Native;

namespace Rask.TestSupport.ScriptFixtures;

/// <summary>What one fixture run has said so far: its report, what it printed, and everything that went wrong.</summary>
internal sealed class FixtureOutcome
{
    private readonly StringBuilder _console = new();
    private readonly Dictionary<JsValue, JsValue> _unhandled = [];
    private readonly List<string> _uncaught = [];
    private string? _entryFailure;

    /// <summary>The JSON the fixture reported, once it has.</summary>
    public string? Report { get; private set; }

    /// <summary>Whether the fixture's module has finished evaluating, top-level awaits and all.</summary>
    public bool EntrySettled { get; private set; }

    public string Console => _console.ToString();

    /// <summary>Why the run cannot be trusted — a throw, an unhandled rejection — or null while nothing has failed.</summary>
    public string? Problem
    {
        get
        {
            if (_uncaught.Count > 0)
            {
                return "Uncaught: " + string.Join(Environment.NewLine, _uncaught);
            }

            if (_entryFailure is not null)
            {
                return "The fixture threw: " + _entryFailure;
            }

            return _unhandled.Count > 0
                ? "Unhandled rejection: " + string.Join(Environment.NewLine, _unhandled.Values.Select(Describe))
                : null;
        }
    }

    /// <returns>False when the fixture had already reported: it reports once.</returns>
    public bool Reported(string json)
    {
        if (Report is not null)
        {
            return false;
        }

        Report = json;
        return true;
    }

    public void Printed(string level, string text) => _console.Append(level).Append(": ").AppendLine(text);

    public void Uncaught(JsValue error) => _uncaught.Add(Describe(error));

    public void Settled(JsValue? failure)
    {
        EntrySettled = true;
        _entryFailure = failure is null ? null : Describe(failure);
    }

    public void Rejected(JsValue promise, JsValue reason) => _unhandled[promise] = reason;

    public void Handled(JsValue promise) => _unhandled.Remove(promise);

    /// <summary>An error's stack (which leads with its message), or the value as text.</summary>
    public static string Describe(JsValue value) =>
        value is JsObject error && error.Get("stack") is { } stack && stack.IsString() ? stack.AsString() : value.ToString();
}
