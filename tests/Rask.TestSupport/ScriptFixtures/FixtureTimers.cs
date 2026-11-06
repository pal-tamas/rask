using Jint.Native;

namespace Rask.TestSupport.ScriptFixtures;

/// <summary>
///     The timers a fixture schedules, on a virtual clock that jumps straight to the next one due.
/// </summary>
/// <remarks>
///     A fixture that waits 300 ms for a module's deadline therefore costs no wall-clock time, and two timers due at
///     the same moment always fire in the order they were set — the ordering a browser and Node both promise, without
///     the scheduler noise that made a real clock the source of a fixture's flakes.
/// </remarks>
internal sealed class FixtureTimers
{
    private readonly PriorityQueue<Timer, (double Due, long Order)> _queue = new();
    private readonly Dictionary<int, Timer> _live = [];
    private long _order;
    private int _lastId;

    /// <summary>Milliseconds since the fixture started, as <c>performance.now()</c> and <c>Date</c> read it.</summary>
    public double Now { get; private set; }

    public int Set(JsValue callback, double delay, bool repeat)
    {
        var timer = new Timer(++_lastId, callback, Clamp(delay), repeat);
        _live.Add(timer.Id, timer);
        Enqueue(timer);
        return timer.Id;
    }

    public void Clear(int id) => _live.Remove(id);

    /// <summary>The next live timer, with the clock moved on to when it is due; false once none is left.</summary>
    public bool TryTakeNext(out JsValue callback)
    {
        while (_queue.TryDequeue(out var timer, out var at))
        {
            if (!_live.ContainsKey(timer.Id))
            {
                continue;
            }

            Now = Math.Max(Now, at.Due);
            if (timer.Repeat)
            {
                Enqueue(timer);
            }
            else
            {
                _live.Remove(timer.Id);
            }

            callback = timer.Callback;
            return true;
        }

        callback = JsValue.Undefined;
        return false;
    }

    /// <summary>
    ///     Node's rule, which the fixtures were written against: below 1 ms (or past a 32-bit int) is 1 ms, so a
    ///     zero-delay timer and a 1 ms one set after it keep the order they were set in.
    /// </summary>
    private static double Clamp(double delay) =>
        double.IsNaN(delay) || delay < 1 || delay > int.MaxValue ? 1 : delay;

    private void Enqueue(Timer timer) => _queue.Enqueue(timer, (Now + timer.Delay, ++_order));

    private sealed record Timer(int Id, JsValue Callback, double Delay, bool Repeat);
}
