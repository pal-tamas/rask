<script setup lang="ts">
// An ordinary Vue single-file component. Nothing here imports Rask, and nothing here knows it is an
// island — the build generates an entry module that pairs this with the adapter.
//
// The props type is GENERATED from VueChart.cs. Rename a C# property and this stops compiling, which
// is the difference between this and embedding Vue by hand.
import type { VueChartProps } from '@rask/VueChart.props'

const props = defineProps<VueChartProps>()

const height = (value: number) => `${Math.max(2, Math.min(100, value))}%`
</script>

<template>
  <figure class="vue-chart" data-testid="vue-chart">
    <figcaption v-if="heading">{{ heading }}</figcaption>
    <div class="bars">
      <button
        v-for="bar in props.series"
        :key="bar.label"
        type="button"
        class="bar"
        :data-label="bar.label"
        :style="{ height: height(bar.value) }"
        @click="props.onBarClick?.(bar.value)"
      >
        <span class="value">{{ bar.value }}</span>
        <span class="label">{{ bar.label }}</span>
      </button>
    </div>
  </figure>
</template>

<style scoped>
.vue-chart { margin: 0; }
.bars { display: flex; align-items: flex-end; gap: 0.5rem; height: 8rem; }
.bar {
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  border: 0;
  border-radius: 0.25rem 0.25rem 0 0;
  background: #4f46e5;
  color: #fff;
  font: inherit;
  font-size: 0.75rem;
  cursor: pointer;
}
.bar:hover { background: #4338ca; }
.label { padding-bottom: 0.25rem; opacity: 0.85; }
</style>
