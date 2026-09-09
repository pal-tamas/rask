using Rask.Core.Forms;

namespace Rask.Core.Tests.Callbacks;

/// <summary>
///     The three carrier families a component's props are declared with, now that the chain receives on
///     the component itself. Their whole job is to hold a delegate WITHOUT being one — see
///     <see cref="CarrierIsNotADelegate" />, which is the property the entire builder surface rests on.
/// </summary>
public class CarrierTests
{
    // The load-bearing fact. C# member lookup stops at a delegate-typed property when it resolves
    // `x.OnClick(fn)` and reads the call as an invocation (CS1593), so an extension setter of the same
    // name is never considered. A struct is not invocable, so lookup falls through and the setter binds.
    // Asserted rather than assumed: if a carrier ever became a delegate, every chain step named after a
    // handler would go unreachable at once, and the failure would be reported at unrelated call sites.
    [Fact]
    public void CarrierIsNotADelegate()
    {
        Assert.False(typeof(Callback).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Callback<int>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Callback<int, int>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Fn<string>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Fn<int, string>).IsSubclassOf(typeof(Delegate)));
        Assert.False(typeof(Validator<string>).IsSubclassOf(typeof(Delegate)));
    }

    // A synchronous handler must not acquire an asynchronous hop it did not have: no Task, no closure,
    // no state machine. `null` is how the slot says "nothing to await", which is what lets a caller
    // write `if (cb?.Invoke() is { } t) await t;` and stay off the async path entirely.
    [Fact]
    public void SyncHandlerRunsAndHandsBackNothingToAwait()
    {
        var ran = false;
        var cb = new Callback(() => ran = true);

        Assert.Null(cb.Invoke());
        Assert.True(ran);
    }

    [Fact]
    public async Task AsyncHandlerHandsBackTheTaskToAwait()
    {
        var ran = false;
        var cb = new Callback(async () =>
        {
            await Task.Yield();
            ran = true;
        });

        var pending = cb.Invoke();

        Assert.NotNull(pending);
        await pending;
        Assert.True(ran);
    }

    // An unset slot is inert rather than throwing, so a component can call its optional callbacks
    // unconditionally.
    [Fact]
    public void UnsetCarrierIsInert()
    {
        Assert.Null(default(Callback).Invoke());
        Assert.Null(default(Callback<int>).Invoke(1));
        Assert.Null(default(Callback<int, int>).Invoke(1, 2));
        Assert.False(default(Callback).HasValue);
        Assert.False(default(Fn<string>).HasValue);
        Assert.Null(default(Fn<string>).Invoke());
        Assert.Null(default(Fn<int, string>).Invoke(1));
    }

    [Fact]
    public void ArgumentCarryingCallbacksPassTheirArguments()
    {
        var seen = 0;
        Assert.Null(new Callback<int>(v => seen = v).Invoke(42));
        Assert.Equal(42, seen);

        var sum = 0;
        Assert.Null(new Callback<int, int>((a, b) => sum = a + b).Invoke(3, 4));
        Assert.Equal(7, sum);
    }

    // The delegate is stored BARE, so the runtime's handler dispatch keeps type-switching on the shape
    // it always did and a sync handler stays an Action all the way down to the DOM event store.
    [Fact]
    public void HandlerRoundTripsTheDelegateUnchanged()
    {
        Action handler = () => { };
        var cb = new Callback(handler);

        Assert.Same(handler, cb.Handler);
        Assert.True(cb.HasValue);
    }

    [Fact]
    public void ValueCarriersReturnWhatTheyAreAsked()
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
    public async Task SyncValidatorCompletesSynchronously()
    {
        var validator = new Validator<string>(new Validate<string>(v => v.Length < 3 ? ["too short"] : []));

        var pending = validator.Invoke("ab", CancellationToken.None);

        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(["too short"], await pending);
    }

    [Fact]
    public async Task AsyncValidatorAwaitsItsRule()
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
    public async Task UnsetValidatorAccepts()
    {
        Assert.Empty(await default(Validator<string>).Invoke("anything", CancellationToken.None));
        Assert.False(default(Validator<string>).HasValue);
    }
}
