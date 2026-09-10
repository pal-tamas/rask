<script setup lang="ts">
// An ordinary Vue single-file component. Nothing here imports Rask, and nothing here knows it is an
// island — the build generates an entry module that pairs this with the adapter.
//
// The props type is GENERATED from VueChart.cs. Rename a C# property and this stops compiling, which
// is the difference between this and embedding Vue by hand.
//
// Styled with TAILWIND rather than a scoped <style> block, on purpose: an island is an ordinary part
// of the page, so it should reach the same design system the C# markup around it uses. The utilities
// below only survive because the app's stylesheet names this directory in an `@source` — Tailwind
// scans the project it runs from, and these files live in a different one.
import type { VueChartProps } from '@rask/VueChart.props'

const props = defineProps<VueChartProps>()

const height = (value: number) => `${Math.max(2, Math.min(100, value))}%`
</script>

<template>
  <figure class="m-0" data-testid="vue-chart">
    <figcaption
      v-if="heading"
      class="mb-2 text-sm font-semibold tracking-wide text-slate-500 dark:text-slate-400"
    >
      {{ heading }}
    </figcaption>

    <div class="flex h-32 items-end gap-2">
      <button
        v-for="bar in props.series"
        :key="bar.label"
        type="button"
        class="flex flex-1 cursor-pointer flex-col justify-end rounded-t bg-indigo-600 text-xs text-white transition-colors hover:bg-indigo-700"
        :data-label="bar.label"
        :style="{ height: height(bar.value) }"
        @click="props.onBarClick?.(bar.value)"
      >
        <span class="font-semibold tabular-nums">{{ bar.value }}</span>
        <span class="pb-1 opacity-85">{{ bar.label }}</span>
      </button>
    </div>
  </figure>
</template>
