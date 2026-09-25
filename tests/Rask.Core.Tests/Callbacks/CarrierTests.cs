using Rask.Core.Forms;

namespace Rask.Core.Tests.Callbacks;

/// <summary>
///     The three carrier families a component's props are declared with, now that the chain receives on
///     the component itself. Their whole job is to hold a delegate WITHOUT being one — see
///     <see cref="A_carrier_is_not_a_delegate" />, which is the property the entire builder surface rests on.
/// </summary>
public class CarrierTests
{
    // The load-bearing fact. C# member lookup stops at a delegate-typed property when it resolves
    // `x.OnClick(fn)` and reads the call as an invocation (CS1593), so an extension setter of the same
    // name is never considered. A struct is not invocable, so lookup falls through and the setter binds.
    // Asserted rather than assumed: if a carrier ever became a delegate, every chain step named after a
    // handler would go unreachable at once, and the failure would be reported at unrelated call sites.
    [Fact]
    public void A_carrier_is_not_a_delegate()
    {
        Assert.False(typeof(Callback).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Callback<int>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Callback<int, int>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Fn<string>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Fn<int, string>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Validator<string>).IsSubclassOf(typeof(Delegate)));
    }

    // A synchronous handler must not acquire an asynchronous hop it did not have: no new Task, no closure,
    // no state machine. It hands back the cached completed task, so a caller's plain `await cb.Invoke();`
    // never yields.
    [Fact]
    public void A_sync_handler_runs_and_hands_back_the_cached_completed_task()
    {
        var ran = false;
        var cb = new Callback(() => ran = true);

        var pending = cb.Invoke();

        Assert.Same(Task.CompletedTask, pending);
        Assert.True(ran);
    }

    [Fact]
    public async Task An_async_handler_hands_back_the_task_to_await()
    {
        var ran = false;
        var cb = new Callback(async () =>
        {
            await Task.Yield();
            ran = true;
        });

        var pending = cb.Invoke();

        await pending;
        Assert.True(ran);
    }

    // An unset slot is inert rather than throwing, so a component can call its optional callbacks
    // unconditionally.
    [Fact]
    public void An_unset_carrier_is_inert()
    {
        Assert.Same(Task.CompletedTask, default(Callback).Invoke());
        Assert.Same(Task.CompletedTask, default(Callback<int>).Invoke(1));
        Assert.Same(Task.CompletedTask, default(Callback<int, int>).Invoke(1, 2));
        Assert.False(default(Callback).HasValue);
        Assert.False(default(Fn<string>).HasValue);
        Assert.Null(default(Fn<string>).Invoke());
        Assert.Null(default(Fn<int, string>).Invoke(1));
    }

    // An event PROPERTY is `Callback?`, and it answers Invoke itself — so firing and forwarding need no `?.`
    // and no `?? Task.CompletedTask`.
    [Fact]
    public async Task An_event_property_invokes_whether_or_not_it_was_set()
    {
        Callback? unset = null;
        Callback<int>? unsetOne = null;
        var seen = 0;
        Callback<int>? set = new Callback<int>(v => seen = v);

        var none = unset.Invoke();
        var noneWithArg = unsetOne.Invoke(1);
        await set.Invoke(7);

        Assert.Same(Task.CompletedTask, none);
        Assert.Same(Task.CompletedTask, noneWithArg);
        Assert.Equal(7, seen);
    }

    [Fact]
    public void Argument_carrying_callbacks_pass_their_arguments()
    {
        var seen = 0;
        Assert.Same(Task.CompletedTask, new Callback<int>(v => seen = v).Invoke(42));
        Assert.Equal(42, seen);

        var sum = 0;
        Assert.Same(Task.CompletedTask, new Callback<int, int>((a, b) => sum = a + b).Invoke(3, 4));
        Assert.Equal(7, sum);
    }

    // The delegate is stored BARE, so the runtime's handler dispatch keeps type-switching on the shape
    // it always did and a sync handler stays an Action all the way down to the DOM event store.
    [Fact]
    public void The_handler_round_trips_the_delegate_unchanged()
    {
        Action handler = () => { };
        var cb = new Callback(handler);

        Assert.Same(handler, cb.Handler);
        Assert.True(cb.HasValue);
    }

    [Fact]
    public void Value_carriers_return_what_they_are_asked()
    {
        Assert.Equal("x", new Fn<string>(() => "x").Invoke());
        Assert.Equal("7", new Fn<int, string>(i => i.ToString()).Invoke(7));
        Assert.Equal("boom", new Fn<Exception, Action, string>((e, _) => e.Message)
            .Invoke(new InvalidOperationException("boom"), () => { }));
        Assert.True(new Fn<int, bool>(i => i > 0).Invoke(1));
    }

    // ValueTask, not Task: the synchronous rule is the common one and runs on every keystroke of every
    // bound control, so it has to complete without allocating.
    [Fact]
    public async Task A_sync_validator_completes_synchronously()
    {
        var validator = new Validator<string>(new Validate<string>(v => v.Length < 3 ? ["too short"] : []));

        var pending = validator.Invoke("ab", CancellationToken.None);

        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(["too short"], await pending);
    }

    [Fact]
    public async Task An_async_validator_awaits_its_rule()
    {
        var validator = new Validator<string>(new ValidateAsync<string>(async (v, ct) =>
        {
            await Task.Yield();
            return v.Length < 3 ? ["too short"] : Enumerable.Empty<string>();
        }));

        Assert.Equal(["too short"], await validator.Invoke("ab", CancellationToken.None));
        Assert.Empty(await validator.Invoke("abcd", CancellationToken.None));
    }

    [Fact]
    public async Task An_unset_validator_accepts_everything()
    {
        Assert.Empty(await default(Validator<string>).Invoke("anything", CancellationToken.None));
        Assert.False(default(Validator<string>).HasValue);
    }
}
