namespace Rask.Core.Forms;

// A choice's change handler behind what it offered: what arrives runs the handler only when a part of the choice the
// render drew — an option, a radio, a checkbox — sends it. Anything else is refused — nothing is written, nothing is
// validated, the field keeps its value — and said once at Warning (OfferedOptions.Refused).
//
// The guard runs the handler the render made, so it checks what that render drew: the page the browser has, or the
// one drawn since. An adapter (IHandlerAdapter): the component that owns the handler, and whether a later render's
// handler is the same one, are read from the handler as written.
internal sealed class OfferedGuard(Delegate inner, OfferedOptions offered) : IHandlerAdapter
{
    public Delegate Inner => inner;

    /// <summary>A handler that takes one value: run only for a value the choice offered.</summary>
    public static Func<string, Task> One(Func<string, Task> handler, OfferedOptions offered) =>
        new OfferedGuard(handler, offered).OneAsync;

    /// <summary>
    ///     The handler of a control drawn disabled, whatever its shape: never run, since the browser sends no event for
    ///     a disabled control, and what arrives anyway is refused and said once.
    /// </summary>
    /// <param name="handler">The handler the control would run while enabled.</param>
    /// <param name="control">What the control is called in the log: <c>input</c>, <c>textarea</c>.</param>
    /// <param name="name">The name it posts under, or null.</param>
    public static Func<string, Task> Refusing(Delegate handler, string control, string? name) =>
        new OfferedGuard(handler, OfferedOptions.Disabled(control, name)).OneAsync;

    /// <summary>
    ///     A handler that takes a whole selection — an <c>Action</c> or a <c>Func</c> of the values: run with the values
    ///     the choice offered, the rest dropped, and not at all for a selection of nothing but values it never offered.
    /// </summary>
    public static Func<IReadOnlyList<string>, Task> Many(Delegate handler, OfferedOptions offered) =>
        new OfferedGuard(handler, offered).ManyAsync;

    private Task OneAsync(string value)
    {
        if (offered.Offers(value))
        {
            return ((Func<string, Task>)inner)(value);
        }

        offered.Refused();
        return Task.CompletedTask;
    }

    private Task ManyAsync(IReadOnlyList<string> values)
    {
        var picked = values;

        // A disabled control takes no selection, an empty one included: clearing is a write too.
        if (offered.Closed || !values.All(offered.Offers))
        {
            offered.Refused();
            picked = [.. values.Where(offered.Offers)];

            // A browser never sends a selection made only of values it was not offered: none of it is the reader's.
            if (picked.Count == 0)
            {
                return Task.CompletedTask;
            }
        }

        if (inner is Action<IReadOnlyList<string>> sync)
        {
            sync(picked);
            return Task.CompletedTask;
        }

        return ((Func<IReadOnlyList<string>, Task>)inner)(picked);
    }
}
