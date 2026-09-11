<!-- An ordinary Svelte 5 component, in runes mode. The props type is generated from SvelteCounter.cs. -->
<script lang="ts">
  import type { SvelteCounterProps } from '@rask/SvelteCounter.props'

  const props: SvelteCounterProps = $props()
  let total = $state(0)

  $effect(() => {
    props.onTotalChanged?.(total)
  })
</script>

<div class="island-counter" data-testid="svelte-counter">
  <div class="caption">{props.caption}</div>
  <button type="button" data-testid="svelte-counter-add" onclick={() => (total += props.step)}>
    add {props.step}
  </button>
  <span class="total">
    total <strong data-testid="svelte-counter-total">{total}</strong>
  </span>
</div>
