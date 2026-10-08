using System.Buffers;
using System.Collections.Frozen;

namespace Rask.Server.Http;

/// <summary>
///     Writes the behaviour hooks' <c>&lt;script&gt;</c> into a first response whose page asks for one.
/// </summary>
/// <remarks>
///     <para>
///         The hooks are a bundle the runtime loads on demand (<c>rask-hook-loader.ts</c>), which for a page that
///         arrives already carrying a hooked attribute would mean a round trip after the runtime ran: a dialog
///         rendered open promoted late, a remembered checkbox corrected late. The server knows what it rendered,
///         so it says so, and the browser fetches the two scripts side by side and runs them back to back, as when
///         they were one.
///     </para>
///     <para>
///         Only ever a saving. A page this misses still loads the bundle, from the runtime; a page this matches
///         by accident (the word in its text) downloads a script it did not need. The tag carries
///         <c>data-rask-managed</c>, so the frame walk does not count it and the morph does not remove it, and it
///         is written into the FIRST response only: the body the session diffs against never contains it.
///     </para>
/// </remarks>
internal static class HookBundleTag
{
    private const string BodyClose = "</body>";

    /// <summary>
    ///     Every attribute that asks for a hook, as it appears in rendered markup. The same list as
    ///     <c>HOOK_ATTRIBUTES</c> in <c>rask-hook-loader.ts</c>; <c>HookBundleTagTests</c> holds the two together.
    /// </summary>
    internal static readonly string[] Attributes =
    [
        "data-rask-tooltip", "data-rask-hover",
        "popover", "commandfor", "data-rask-modal", "data-rask-modal-open",
        "data-rask-lock",
        "data-rask-menu-pointer", "data-rask-safe-area",
        "data-rask-copy", "data-rask-clear", "data-rask-focus", "data-rask-mask", "data-rask-mask-money",
        "data-rask-big-step", "role",
        "data-rask-contain-keys", "data-rask-listbox-button", "data-rask-roving",
        "data-rask-focus-follows", "data-rask-press-keeps-focus", "aria-activedescendant",
        "data-rask-toggle",
        "data-rask-drag",
        "data-rask-requires",
        "data-rask-plot", "data-rask-measure",
        "data-rask-otp",
        "data-rask-segments",
        "data-rask-dismiss-scope", "data-rask-stack",
        "data-rask-persist", "data-rask-uncheck-on-navigate",
        "data-rask-carousel", "data-rask-carousel-controls",
    ];

    // An attribute follows a space. The search finds where one of the names STARTS; whether it is that name and
    // not a longer one (data-rask-focus-trap is the runtime's own, not data-rask-focus) is settled at each hit.
    private static readonly SearchValues<string> Candidates = SearchValues.Create(
        [.. Attributes.Select(name => " " + name)], StringComparison.Ordinal);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Names =
        Attributes.ToFrozenSet(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    ///     <paramref name="html" /> with the bundle's tag as the last thing in <c>&lt;body&gt;</c> — after the
    ///     runtime's own, which is what it leans on — when the page asks for a hook; otherwise the same string.
    /// </summary>
    /// <param name="html">The page as it was rendered.</param>
    /// <param name="pathBase">The app's path base, or empty.</param>
    /// <param name="scriptUrl">The bundle's URL under that base, with its version.</param>
    internal static string AddTo(string html, string pathBase, string scriptUrl)
    {
        if (!AsksForAHook(html))
        {
            return html;
        }

        // The LAST one: a </body> can appear inside a script's string.
        var bodyClose = html.LastIndexOf(BodyClose, StringComparison.Ordinal);
        return bodyClose < 0
            ? html
            : string.Concat(
                html.AsSpan(0, bodyClose),
                "<script src=\"",
                System.Net.WebUtility.HtmlEncode(pathBase + scriptUrl),
                string.Concat("\" data-rask-hooks data-rask-managed></script>", html.AsSpan(bodyClose)));
    }

    private static bool AsksForAHook(ReadOnlySpan<char> html)
    {
        while (html.IndexOfAny(Candidates) is >= 0 and var at)
        {
            var rest = html[(at + 1)..];
            var length = rest.IndexOfAnyExcept(NameCharacters);
            var name = length < 0 ? rest : rest[..length];
            html = rest[name.Length..];

            // A role asks for a hook only when it is the switch role, which Enter toggles.
            if (Names.Contains(name) && (!name.SequenceEqual("role") || html.StartsWith("=\"switch\"", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly SearchValues<char> NameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz-");
}
