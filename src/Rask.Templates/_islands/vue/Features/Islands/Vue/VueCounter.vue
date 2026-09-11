<!-- An ordinary Vue single-file component. The props type is generated from VueCounter.cs. -->
<script setup lang="ts">
import { ref, watch } from 'vue'
import type { VueCounterProps } from '@rask/VueCounter.props'

const props = defineProps<VueCounterProps>()
const total = ref(0)

watch(total, value => props.onTotalChanged?.(value))
</script>

<template>
  <div class="island-counter" data-testid="vue-counter">
    <div class="caption">{{ props.caption }}</div>
    <button type="button" data-testid="vue-counter-add" @click="total += props.step">
      add {{ props.step }}
    </button>
    <span class="total">
      total <strong data-testid="vue-counter-total">{{ total }}</strong>
    </span>
  </div>
</template>
