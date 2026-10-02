using Rask.Core.Browser;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Tests.Interop;

namespace Rask.Core.Tests.Browser;

// A browser push arrives through a static [JSInvokable] that cannot tell which session called it. On the
// Server host one process serves every session, so a watch id posted from another socket must not feed
// someone else's handler.
public class BrowserCallbackOwnershipTests
{
    [Fact]
    public async Task A_watch_answers_only_the_session_that_registered_it()
    {
        var mine = new FakeJsRuntime();
        MotionReading? seen = null;
        var id = DeviceMotionInterop.Register(mine, reading =>
        {
            seen = reading;
            return Task.CompletedTask;
        });
        var reading = new MotionReading(0.1, 0.2, 0.3, null, null, null, 16);

        using (JsCaller.Enter(new FakeJsRuntime()))
        {
            await DeviceMotionInterop.Reading(id, reading);
        }

        var fromAnotherSession = seen;
        using (JsCaller.Enter(mine))
        {
            await DeviceMotionInterop.Reading(id, reading);
        }

        DeviceMotionInterop.Unregister(id);
        Assert.Null(fromAnotherSession);
        Assert.Same(reading, seen);
    }

    [Fact]
    public async Task A_one_shot_gesture_result_survives_a_post_from_another_session()
    {
        var calls = 0;
        var id = GestureResultInterop.Register(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        using (JsCaller.Enter(new FakeJsRuntime()))
        {
            await GestureResultInterop.Result(id, "accepted");
        }

        var fromAnotherSession = calls;
        await GestureResultInterop.Result(id, "accepted");
        await GestureResultInterop.Result(id, "accepted");

        Assert.Equal(0, fromAnotherSession);
        Assert.Equal(1, calls);
    }
}
