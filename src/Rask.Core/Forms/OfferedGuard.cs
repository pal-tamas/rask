namespace Rask.Core.Forms;

// A select's change handler behind the options it offered: what arrives runs the handler only when an option the
// select drew sends it. Anything else is refused — nothing is written, nothing is validated, the field keeps its
// value — and said once at Warning (OfferedOptions.Refused).
//
// The guard runs the handler the render made, so it checks the options that render drew: the page the browser
// has, or the one drawn since. An adapter (IHandlerAdapter): the component that owns the handler, and whether a
// later render's handler is the same one, are read from the handler as written.
internal sealed class OfferedGuard(Delegate inner, OfferedOptions offered, string? name) : IHandlerAdapter
{
    public Delegate Inner => inner;

    /// <summary>A handler that takes one value: run only for a value an option offered.</summary>
    public static Func<string, Task> One(Func<string, Task> handler, OfferedOptions offered, string? name) =>
        new OfferedGuard(handler, offered, name).OneAsync;

    /// <summary>A handler that takes a whole selection: run with the values options offered, the rest dropped.</summary>
    public static Func<IReadOnlyList<string>, Task> Many(
        Func<IReadOnlyList<string>, Task> handler, OfferedOptions offered, string? name) =>
        new OfferedGuard(handler, offered, name).ManyAsync;

    private Task OneAsync(string value)
    {
        if (offered.Offers(value))
        {
            return ((Func<string, Task>)inner)(value);
        }

        OfferedOptions.Refused(name);
        return Task.CompletedTask;
    }

    private Task ManyAsync(IReadOnlyList<string> values)
    {
        var picked = values;
        if (!values.All(offered.Offers))
        {
            picked = [.. values.Where(offered.Offers)];
            OfferedOptions.Refused(name);
        }

        return ((Func<IReadOnlyList<string>, Task>)inner)(picked);
    }
}
