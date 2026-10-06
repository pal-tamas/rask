<!-- The island SvelteAdapterFixture.ts mounts: an ordinary Svelte 5 component, no Rask import in it. -->
<script lang="ts">
  import { onMount } from 'svelte'

  let { heading = 'untitled', onPointClick, children } = $props()

  // State the component owns and Rask knows nothing about. It is what a remount would destroy.
  let count = $state(0)

  onMount(() => {
    globalThis.__svelteFixture.effectRuns++
    return () => {
      globalThis.__svelteFixture.cleanupRuns++
    }
  })
</script>

<div>
  <h2 id="heading">{heading}</h2>
  <span id="count">{count}</span>
  <button id="bump" onclick={() => count++}>bump</button>
  <button id="ping" onclick={() => onPointClick?.(42)}>ping</button>
  <div id="slot">{@render children?.()}</div>
</div>
