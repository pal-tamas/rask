<script lang="ts">
  // An ordinary Svelte 5 component. Nothing here imports Rask.
  //
  // The props type is GENERATED from SvelteMeter.cs, so renaming a C# property stops this compiling.
  import type { SvelteMeterProps } from '@rask/SvelteMeter.props'

  let { value, label }: SvelteMeterProps = $props()

  // State this component owns and C# never sees. It is the proof that an update is a RECONCILE: when
  // C# re-renders a new `value`, this must keep its count rather than starting over.
  let nudges = $state(0)
</script>

<div class="meter" data-testid="svelte-meter">
  <div class="row">
    <span class="label">{label}</span>
    <span class="value" data-testid="meter-value">{value}</span>
  </div>

  <div class="track">
    <div class="fill" style="width: {Math.max(0, Math.min(100, value))}%"></div>
  </div>

  <button type="button" data-testid="meter-nudge" onclick={() => nudges++}>
    nudged <span data-testid="meter-nudges">{nudges}</span> times
  </button>
</div>

<style>
  .meter { display: flex; flex-direction: column; gap: 0.5rem; }
  .row { display: flex; justify-content: space-between; font-size: 0.875rem; }
  .value { font-variant-numeric: tabular-nums; font-weight: 600; }
  .track { height: 0.5rem; border-radius: 999px; background: #e2e8f0; overflow: hidden; }
  .fill { height: 100%; background: #0d9488; transition: width 150ms ease; }
  button { align-self: flex-start; font: inherit; font-size: 0.75rem; cursor: pointer; }
</style>
