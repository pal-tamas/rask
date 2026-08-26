using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Globalization;

/// <summary>
///     The culture in effect right now, for code that is not a component — a helper, a formatter, a
///     library type like <c>Rask.Bootstrap</c>'s pickers.
/// </summary>
/// <remarks>
///     <para>
///         <b>Reading <see cref="Current" /> marks the component being rendered as depending on the
///         culture</b>, which permanently opts it out of the clean-subtree render cache. That is the
///         point of routing every read through here rather than letting callers touch
///         <c>CultureInfo.CurrentCulture</c>: the marking cannot be forgotten, because it happens inside
///         the only accessor. Without it, switching language would leave cached subtrees on screen still
///         rendered in the old one. Same contract as <c>Context.Get</c> and <c>EditContext</c>.
///     </para>
///     <para>
///         <b>Outside a render walk or a handler dispatch, the current culture is undefined for Rask
///         purposes</b> and this falls back to the thread's. That is not an oversight: the session, not
///         the thread, owns the culture, and there is no session to ask from a background timer.
///     </para>
/// </remarks>
public static class RaskCulture
{
    /// <summary>
    ///     Whether any host has configured languages. A pure fast path — when false, the render walk
    ///     never resolves a culture service and every accessor here is a thread-culture read.
    /// </summary>
    /// <remarks>
    ///     Process-wide rather than per-host, which the repo normally avoids. It is safe precisely
    ///     because it is not a source of truth: it only decides whether to <em>look</em> for one. Two
    ///     hosts in one process take the union — both then pay a lookup, and neither can read the
    ///     other's culture, because the value itself always comes from the session.
    /// </remarks>
    public static bool IsEnabled { get; internal set; }

    /// <summary>
    ///     The culture to format with, and a declaration that the calling component depends on it.
    /// </summary>
    public static CultureInfo Current
    {
        get
        {
            if (!IsEnabled)
            {
                return CultureInfo.CurrentCulture;
            }

            var ctx = LiveRenderContext.CurrentSync;
            if (ctx is not null)
            {
                ctx.MarkCurrentReadsAmbientState();
                return ctx.Culture;
            }

            if (RaskCultureScope.Current is { } scope)
            {
                return scope.Culture;
            }

            // An async continuation inside a render: the ThreadStatic mirror is gone but the
            // AsyncLocal context is not. No marking here — the walk that could act on it has ended.
            return LiveRenderContext.Current?.Culture ?? CultureInfo.CurrentCulture;
        }
    }

    /// <summary>The language to render text in, and a declaration that the caller depends on it.</summary>
    public static CultureInfo CurrentUI
    {
        get
        {
            if (!IsEnabled)
            {
                return CultureInfo.CurrentUICulture;
            }

            var ctx = LiveRenderContext.CurrentSync;
            if (ctx is not null)
            {
                ctx.MarkCurrentReadsAmbientState();
                return ctx.UICulture;
            }

            if (RaskCultureScope.Current is { } scope)
            {
                return scope.UICulture;
            }

            return LiveRenderContext.Current?.UICulture ?? CultureInfo.CurrentUICulture;
        }
    }

    /// <summary>Whether the current culture is written right-to-left.</summary>
    public static bool IsRightToLeft => Current.TextInfo.IsRightToLeft;

    /// <summary>
    ///     The value for <c>&lt;html lang&gt;</c>, or <c>null</c> when culture support is off so the
    ///     document keeps its existing default.
    /// </summary>
    /// <remarks>
    ///     Returning <c>null</c> rather than the negotiated name when nothing is configured is what keeps
    ///     every existing app's HTML byte-for-byte identical: otherwise <c>lang="en"</c> would silently
    ///     become <c>lang="en-US"</c> on any US-locale machine and churn every golden file and E2E
    ///     assertion in the repo.
    /// </remarks>
    public static string? HtmlLang
    {
        get
        {
            // Deliberately NOT gated on IsEnabled alone. That flag says some host in this process
            // configured cultures, which is not a fact about the render in front of you: a static
            // ToHtml or a unit test has no session, and reporting the machine's locale there would
            // turn lang="en" into lang="en-US" on a US machine — changing the HTML of apps that never
            // asked for localization, and making the answer depend on what else the process is doing.
            if (LiveRenderContext.CurrentSync is not { HasCulture: true } ctx)
            {
                return null;
            }

            var name = ctx.UICulture.Name;
            return name.Length == 0 ? null : name;
        }
    }

    /// <summary>
    ///     <c>"rtl"</c> for a right-to-left culture, otherwise <c>null</c> so no <c>dir</c> attribute is
    ///     emitted at all — left-to-right is the HTML default, and emitting it would change every
    ///     existing page.
    /// </summary>
    public static string? HtmlDir =>
        LiveRenderContext.CurrentSync is { HasCulture: true } ctx && ctx.Culture.TextInfo.IsRightToLeft
            ? "rtl"
            : null;

    /// <summary>
    ///     The current culture <em>without</em> marking the caller as culture-dependent, for framework
    ///     internals that must not latch the render cache.
    /// </summary>
    internal static CultureInfo CurrentUnmarked
    {
        get
        {
            if (!IsEnabled)
            {
                return CultureInfo.CurrentCulture;
            }

            return LiveRenderContext.CurrentSync?.Culture
                   ?? RaskCultureScope.Current?.Culture
                   ?? LiveRenderContext.Current?.Culture
                   ?? CultureInfo.CurrentCulture;
        }
    }

    /// <summary>Test-only: returns the process to the "no app configured a culture" state.</summary>
    internal static void ResetForTests() => IsEnabled = false;
}
