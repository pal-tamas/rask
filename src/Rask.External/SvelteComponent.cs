namespace Rask.External;

/// <summary>
///     A Rask component rendered by Svelte, from the <c>.svelte</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         A single-file component is compiled by a <em>Vite plugin</em> rather than by a compiler of
///         its own, which is what makes this an adapter rather than a toolchain: the build adds
///         <c>@sveltejs/vite-plugin-svelte</c> when — and only when — a Svelte island is present.
///     </para>
///     <para>
///         The front-end file stays an ordinary Svelte component with no Rask import in it: the build
///         generates an entry module that pairs it with the adapter, so the <c>.svelte</c> remains
///         portable and testable on its own.
///     </para>
///     <para>
///         Svelte 5 is the target. Props are declared with <c>$props()</c>, and the adapter drives
///         updates through a <c>$state</c> object rather than re-creating the component — so an
///         update reconciles rather than remounting, and the component keeps its scroll position, its
///         focus and its half-typed field.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Chart.cs
///     public sealed partial class Chart : SvelteComponent
///     {
///         public required IReadOnlyList&lt;Point&gt; Series { get; set; }
///         public Action&lt;int&gt;? OnPointClick { get; set; }
///     }
///
///     // Chart.svelte
///     &lt;script lang="ts"&gt;
///     import type { ChartProps } from '@rask/Chart.props'
///     let { series, onPointClick }: ChartProps = $props()
///     &lt;/script&gt;
///     </code>
/// </example>
public abstract partial class SvelteComponent : ExternalComponent
{
    /// <inheritdoc />
    protected sealed override string Runtime => "svelte";
}
