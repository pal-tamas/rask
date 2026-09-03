namespace Rask.External;

/// <summary>
///     A Rask component rendered by Solid, from the <c>.tsx</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         Solid compiles JSX to real DOM operations rather than to a virtual tree, so its adapter is
///         the one that most nearly matches what Rask itself does — an update touches the node that
///         changed and nothing else. Props reach the component through a store rather than a re-render,
///         which is what keeps that fine-grained: <c>props.series</c> is a getter Solid tracks, and
///         re-creating the component to show new props would throw away every signal inside it.
///     </para>
///     <para>
///         <strong>A JSX island's helpers belong in the island's own directory.</strong> Solid, React
///         and Preact all claim <c>.tsx</c>, so when two of them are present the build scopes each
///         Vite plugin to the directories that runtime's islands live in. That scoping is what makes
///         a helper module compile with its island rather than with the other framework — and getting
///         it wrong is silent, so the build refuses two JSX runtimes in one directory rather than
///         emitting a bundle where a Preact vnode is handed to Solid's renderer.
///     </para>
///     <para>
///         The front-end file stays an ordinary Solid component with no Rask import in it: the build
///         generates an entry module that pairs it with the adapter, so the <c>.tsx</c> remains
///         portable and testable on its own.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Chart.cs
///     public sealed partial class Chart : SolidComponent
///     {
///         public required IReadOnlyList&lt;Point&gt; Series { get; set; }
///         public Action&lt;int&gt;? OnPointClick { get; set; }
///     }
///
///     // Chart.tsx
///     import type { ChartProps } from '@rask/Chart.props'
///     // Destructuring would read every prop once and freeze it — Solid tracks the ACCESS.
///     export default function Chart(props: ChartProps) { … props.series … }
///     </code>
/// </example>
public abstract partial class SolidComponent : ExternalComponent
{
    /// <inheritdoc />
    protected sealed override string Runtime => "solid";
}
