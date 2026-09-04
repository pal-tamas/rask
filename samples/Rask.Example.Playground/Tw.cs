namespace Rask.Example.Playground;

/// <summary>
/// The playground's control styling, as Tailwind utilities.
/// </summary>
/// <remarks>
/// Constants rather than a component per control: the playground has five controls in total, and a
/// wrapper component for each would be more machinery than the thing it wraps. The behavioural hooks
/// (<c>pg-run</c>, <c>pg-prev</c>, …) stay separate class names because the shortcut handler and the E2E
/// select on them — those are behaviour, and mixing them into a styling constant would make a restyle
/// able to break a test in a way that reads as a styling change.
/// </remarks>
internal static class Tw
{
    /// <summary>A secondary control: the outline button language the toolbar is built from.</summary>
    public const string Button =
        "inline-flex items-center gap-1.5 rounded-md border border-white/15 px-2.5 py-1.5 text-xs "
        + "font-medium text-slate-300 no-underline hover:border-white/30 hover:text-white "
        + "disabled:cursor-default disabled:opacity-40";

    /// <summary>The one affirmative action on the page: Run.</summary>
    public const string Primary =
        "inline-flex items-center gap-1.5 rounded-md bg-violet-500 px-2.5 py-1.5 text-xs font-medium "
        + "text-white no-underline hover:bg-violet-400 disabled:cursor-default disabled:opacity-40";
}
