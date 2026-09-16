namespace Rask.Site;

/// <summary>
///     The site's two horizontal containers, stated once.
/// </summary>
/// <remarks>
///     Constants rather than <c>@apply</c>: a constant is read by the compiler, renamed by the IDE and
///     found by Tailwind's scanner like any other literal, while <c>@apply</c> moves the decision into a
///     stylesheet Tailwind then has to be told about. They live here because the top bar is now shared by
///     the landing page and the docs, and a column width that two files each spell out is a column width
///     that drifts by a rem on one of them.
/// </remarks>
internal static class SiteLayout
{
    /// <summary>
    ///     The landing page's column: centred, 1100px, with the gutters every section on <c>/</c> uses.
    /// </summary>
    internal const string Wrap = "mx-auto w-full max-w-[1100px] px-5 sm:px-6";

    /// <summary>
    ///     The docs' full-width row.
    /// </summary>
    /// <remarks>
    ///     <c>/docs</c> has a 280px rail down its left and a 1280px page beside it, so
    ///     <see cref="Wrap" /> would centre the wordmark over the middle of the sidebar. The gutters are
    ///     the ones <c>page-main</c> uses, so the bar's contents line up with the page under it.
    /// </remarks>
    internal const string FullBleed = "w-full px-3 md:px-5";
}
