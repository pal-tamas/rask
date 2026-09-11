using System.Collections.Frozen;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     The island runtimes <c>rask new --islands</c> can scaffold, and the two rules that constrain
///     which of them can share a project.
/// </summary>
/// <remarks>
///     <para>
///         Eight kinds. Seven are front-end frameworks bundled by Vite (<c>Rask.External</c>); the
///         eighth is Blazor, which has no npm side at all — the Razor SDK compiles the <c>.razor</c> and
///         Rask renders it server-side into the first response.
///     </para>
///     <para>
///         Unlike a template, islands are not one fixed tree: they compose, so a project can hold
///         several and the set is chosen per scaffold. That is why they live as fragments under
///         <c>src/Rask.Templates/_islands/</c> and are merged rather than materialised whole.
///     </para>
/// </remarks>
internal static class IslandRuntimes
{
    /// <summary>Every runtime <c>--islands</c> accepts, in the order help prints them.</summary>
    public static readonly string[] All =
        ["react", "preact", "vue", "svelte", "solid", "lit", "angular", "blazor"];

    /// <summary>The runtimes that pair with a <c>.tsx</c>, and therefore need a folder of their own.</summary>
    /// <remarks>
    ///     Three runtimes write the same extension, so a single tree holding two of them is ambiguous:
    ///     <c>ExternalBuildPlan.Overlaps</c> refuses overlapping trees for runtimes that share one. The
    ///     scaffold puts each in <c>Features/Islands/&lt;Runtime&gt;/</c> so two can coexist.
    /// </remarks>
    public static readonly FrozenSet<string> Jsx =
        new[] { "react", "preact", "solid" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The one runtime with no npm dependencies, and therefore no Node requirement.</summary>
    public const string Blazor = "blazor";

    /// <summary>
    ///     Why <paramref name="runtimes"/> cannot share one project, or null when they can.
    /// </summary>
    /// <remarks>
    ///     React and Preact is the pair that cannot: <c>@vitejs/plugin-react</c> resolves Babel 8 while
    ///     <c>@preact/preset-vite</c> pins a <c>@babel/core</c> 7 peer, so <b>npm refuses to install
    ///     both</b>. The build already refuses it (ExternalBuildPlan), but failing here is better: a
    ///     scaffold that succeeds and then cannot install is a project directory the user has to delete.
    /// </remarks>
    public static string? Refuse(IReadOnlyCollection<string> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        if (runtimes.Contains("react") && runtimes.Contains("preact"))
        {
            return "--islands cannot take both react and preact. Their Vite plugins cannot be installed "
                + "side by side — @vitejs/plugin-react resolves Babel 8 while @preact/preset-vite pins "
                + "@babel/core 7 — so npm refuses the install. Pick one: a Preact component can also be "
                + "rendered by ReactComponent if the app aliases react to preact/compat.";
        }

        var unknown = runtimes
            .Where(r => !All.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return unknown.Length > 0
            ? $"Unknown island runtime(s): {string.Join(", ", unknown)}. Choose from {string.Join(", ", All)}."
            : null;
    }
}
