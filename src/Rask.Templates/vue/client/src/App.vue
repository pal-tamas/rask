<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { rask } from './rask/client'
import { getGreeting, recordVisit } from './rask/messages'
import type { Greeting } from './rask/contracts'

const name = ref('world')
const greeting = ref<Greeting | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)

// watch with immediate, not a one-off call: that is what re-reads the ref and refetches when
// it changes. The AbortController is what stops a slow earlier request landing after a later
// one and showing the wrong answer.
let inFlight: AbortController | null = null

async function load() {
  inFlight?.abort()
  const controller = new AbortController()
  inFlight = controller
  error.value = null

  try {
    greeting.value = await rask.dispatch(getGreeting({ name: name.value }), {
      signal: controller.signal,
    })
  } catch (e) {
    if (!controller.signal.aborted) error.value = e instanceof Error ? e.message : String(e)
  }
}

watch(name, load, { immediate: true })

async function visit() {
  busy.value = true
  try {
    await rask.dispatch(recordVisit({ name: name.value }))
    await load()
  } finally {
    busy.value = false
  }
}

const serverTime = computed(() =>
  greeting.value
    ? new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.value.seenAt)
    : '',
)
</script>

<template>
  <div class="flex min-h-screen flex-col bg-base-200">
    <nav class="navbar bg-base-100 shadow-sm">
      <div class="navbar-start">
        <span class="px-2 text-lg font-semibold tracking-tight">Rask + Vue</span>
      </div>
      <div class="navbar-end">
        <a class="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
      </div>
    </nav>

    <main class="hero grow py-16">
      <div class="hero-content text-center">
        <div class="max-w-md">
          <h1 class="text-4xl font-bold">Rask + Vue</h1>
          <p class="py-4 text-base-content/70">One query and one command, over your C# records.</p>

          <div class="card bg-base-100 w-full max-w-md shadow-sm">
            <div class="card-body gap-4 text-left">
              <label class="fieldset">
                <span class="fieldset-legend">Name</span>
                <input class="input w-full" v-model="name" />
              </label>

              <span
                v-if="!greeting && !error"
                class="loading loading-spinner loading-sm"
                aria-label="Loading"
              />
              <div v-else-if="error" role="alert" class="alert alert-error">
                <span>{{ error }}</span>
              </div>

              <template v-if="greeting">
                <p>{{ greeting.message }}</p>
                <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
                <p class="text-sm text-base-content/70">Server time: {{ serverTime }}</p>
                <div class="stat p-0">
                  <div class="stat-title">Visits</div>
                  <div class="stat-value text-2xl">{{ greeting.visits }}</div>
                </div>
              </template>

              <div class="card-actions justify-end">
                <button class="btn btn-primary" :disabled="busy" @click="visit">
                  Record a visit
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </main>

    <footer class="footer footer-center bg-base-100 p-4 text-base-content/70">
      <aside><p>Built with Rask.</p></aside>
    </footer>
  </div>
</template>
