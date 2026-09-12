<!-- rask-external Svelte host, vendored from Rask.External. -->
<!--
  Renders an island's component with its props and, when C# gave it any, its children as the `children`
  snippet. Passed as an explicit prop rather than as content, so the component is never re-created when
  children appear or disappear: content would need an {#if} branch, and switching branches remounts.

  You own this file. It is refreshed on build only while the header line above is intact.
-->
<script lang="ts">
  import RaskTree from './RaskTree.svelte'

  let { host } = $props()
</script>

{#snippet kids()}<RaskTree nodes={host.children} />{/snippet}

<host.component {...host.props} children={host.children ? kids : undefined} />
