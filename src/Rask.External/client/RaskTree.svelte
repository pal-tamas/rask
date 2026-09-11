<!-- rask-external Svelte children, vendored from Rask.External. -->
<!--
  An island's children: text as text, a child island as its component with its own props and children.
  Keyed by the child's C# key where it has one, so a reordered keyed list moves components rather than
  re-creating them; by position otherwise.

  You own this file. It is refreshed on build only while the header line above is intact.
-->
<script lang="ts">
  import RaskTree from './RaskTree.svelte'

  let { nodes } = $props()
</script>

{#each nodes as node, i (typeof node === 'string' ? `#${i}` : (node.key ?? `@${i}`))}
  {#if typeof node === 'string'}{node}{:else}
    {#snippet kids()}<RaskTree nodes={node.children} />{/snippet}
    <node.component {...node.props} children={node.children ? kids : undefined} />
  {/if}
{/each}
