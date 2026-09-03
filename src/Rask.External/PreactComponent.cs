namespace Rask.External;

/// <summary>
///     A Rask component rendered by Preact, from the <c>.tsx</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         Preact used to be reached through <see cref="ReactComponent" /> and <c>preact/compat</c>
///         aliasing, and that still works for an app already built that way. This is the direct route:
///         the island imports from <c>preact</c> itself, the adapter renders with Preact's own
///         <c>render</c>, and nothing in the bundle pretends to be React.
///     </para>
///     <para>
///         <strong>Preact and React islands cannot share a project.</strong> Not a Rask rule — the two
///         Vite plugins cannot be installed together at all: <c>@vitejs/plugin-react</c> wants Babel 8
///         and <c>@preact/preset-vite</c> pins a <c>@babel/core@"7.x"</c> peer, so npm refuses the
///         install before any of this is reached. The build says so by name rather than letting the
///         package manager explain it in terms of neither framework.
///     </para>
///     <para>
///         Preact and Solid <em>can</em> share one, and so can Preact with Vue, Svelte, Lit or
///         Angular. Both JSX runtimes then need their islands in directories of their own — see
///         <c>docs/islands.md</c>, and <see cref="SolidComponent" /> for why.
///     </para>
///     <para>
///         The front-end file stays an ordinary Preact component with no Rask import in it: the build
///         generates an entry module that pairs it with the adapter, so the <c>.tsx</c> remains
///         portable and testable on its own.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Chart.cs
///     public sealed partial class Chart : PreactComponent
///     {
///         public required IReadOnlyList&lt;Point&gt; Series { get; set; }
///         public Action&lt;int&gt;? OnPointClick { get; set; }
///     }
///
///     // Chart.tsx
///     import type { ChartProps } from '@rask/Chart.props'
///     export default function Chart({ series, onPointClick }: ChartProps) { … }
///     </code>
/// </example>
public abstract partial class PreactComponent : ExternalComponent
{
    /// <inheritdoc />
    protected sealed override string Runtime => "preact";
}
