<!-- Rendered only as a CHILD island, inside FixtureCounter's `children` snippet. -->
<script lang="ts">
  import { onMount } from 'svelte'

  let { label } = $props()

  // State the child owns. A parent update that re-created the child would reset it.
  let clicks = $state(0)

  onMount(() => {
    globalThis.__svelteFixture.badgeEffects++
    return () => {
      globalThis.__svelteFixture.badgeCleanups++
    }
  })
</script>

<button id="badge" onclick={() => clicks++}>{label}:{clicks}</button>
