namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     A counter rendered by <c>ReactCounter.tsx</c> — an ordinary React component used as an ordinary
///     Rask component.
/// </summary>
/// <remarks>
///     <para>
///         The same base class covers <b>Preact</b> unchanged. A Preact project aliases <c>react</c> and
///         <c>react-dom</c> to <c>preact/compat</c> in both tsconfig and the Vite plugin, so this adapter
///         type-checks and bundles against either and Rask never needs to know which it got. That is an
///         app-wide choice rather than a per-component one, which is why this showcase is React: one
///         bundle resolves <c>react</c> one way, for every island in it.
///     </para>
///     <para>
///         Keeps a <c>useState</c> of its own that C# never sees — the React half of the same
///         reconcile-not-remount proof <see cref="SvelteMeter" /> makes.
///     </para>
/// </remarks>
public sealed partial class ReactCounter : Rask.External.ReactComponent
{
    /// <summary>The step C# hands it, which the component adds on each press.</summary>
    public int Step { get; set; }

    /// <summary>The caption above the counter.</summary>
    public required string Caption { get; set; }

    /// <summary>Runs with the component's running total whenever it changes.</summary>
    public Action<int>? OnTotalChanged { get; set; }
}
