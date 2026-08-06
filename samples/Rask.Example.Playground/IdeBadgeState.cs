namespace Rask.Example.Playground;

/// <summary>Where the in-browser Roslyn workspace has got to, as shown by the readiness pill.</summary>
internal enum IdeState
{
    Loading,
    Ready,
    Unavailable
}

/// <summary>
///     The class that carries <see cref="IdeState" /> into the DOM, so the pill's state is readable from
///     the markup and not only from its colour and its label.
/// </summary>
/// <remarks>
///     This exists as its own type because it is a <b>test contract</b>, not decoration. The E2E waits for
///     <c>.pg-ide.is-ready</c> to know the workspace has finished pulling its references — and #470, which
///     turned the pill into a <c>BsBadge</c>, dropped these classes in favour of a Bootstrap colour. The
///     selector then matched nothing, and the browser gate was red on <c>main</c> for weeks (#593): a
///     Playwright locator that never resolves fails by <i>timing out</i>, so the report blamed the step
///     that downloads the reference assemblies rather than the missing class.
///     <para>
///         Keeping the mapping here lets <c>Rask.Example.Playground.Tests</c> link this one file and pin
///         it, so the next redesign that drops a state hook fails the fast unit gate with a message that
///         says so, instead of a 180-second timeout in a suite people have learned to skip. The same
///         reasoning as <c>.pg-run</c>, which <c>PlaygroundView.css</c> already documents as a class kept
///         purely as a hook.
///     </para>
/// </remarks>
internal static class IdeBadgeState
{
    public const string Loading = "is-loading";
    public const string Ready = "is-ready";
    public const string Unavailable = "is-off";

    /// <summary>Every state class the pill can render — what a selector has to be one of to resolve.</summary>
    public static readonly string[] All = [Loading, Ready, Unavailable];

    public static string ClassFor(IdeState state) => state switch
    {
        IdeState.Ready => Ready,
        IdeState.Unavailable => Unavailable,
        _ => Loading
    };
}
