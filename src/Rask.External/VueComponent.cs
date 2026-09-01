namespace Rask.External;

/// <summary>
///     A Rask component rendered by Vue, from the <c>.vue</c> file beside it.
/// </summary>
/// <remarks>
///     <para>
///         A single-file component is compiled by a <em>Vite plugin</em> rather than by a compiler of
///         its own, which is what makes this an adapter rather than a toolchain: the build adds
///         <c>@vitejs/plugin-vue</c> when — and only when — a Vue island is present.
///     </para>
///     <para>
///         The front-end file stays an ordinary Vue component with no Rask import in it: the build
///         generates an entry module that pairs it with the adapter, so the <c>.vue</c> remains
///         portable and testable on its own.
///     </para>
///     <para>
///         The module <strong>default-exports the component</strong>, which is what a
///         <c>&lt;script setup&gt;</c> block already does.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Chart.cs
///     public sealed partial class Chart : VueComponent
///     {
///         public required IReadOnlyList&lt;Point&gt; Series { get; set; }
///         public Action&lt;int&gt;? OnPointClick { get; set; }
///     }
///
///     // Chart.vue
///     &lt;script setup lang="ts"&gt;
///     import type { ChartProps } from '@rask/Chart.props'
///     defineProps&lt;ChartProps&gt;()
///     &lt;/script&gt;
///     </code>
/// </example>
public abstract partial class VueComponent : ExternalComponent
{
    /// <inheritdoc />
    protected sealed override string Runtime => "vue";
}
