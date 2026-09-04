<script setup lang="ts">
// The point of this sample in one import: `@rask/messages` is generated from the C# records in
// Features/Hello, and `@rask/client` is the dispatcher that carries them. Rename a property on the
// server and this file stops compiling — there is no schema to keep in sync and no URL written here.
import { createDispatcher, httpTransport, rask } from '@rask/client'
import { getGreeting, recordVisit } from '@rask/messages'

// Rask's typed browser APIs are the same specifier, and the same TypeScript the Server and WASM
// clients run. Imported at module scope on purpose: this file is loaded by NODE during the server
// render before it is ever loaded by a browser, so a module that touched `window` on import would
// crash the page rather than degrade.
import { prefersDark } from '@rask/browser/mediaQuery'

const name = 'meta'

// useAsyncData runs on the SERVER during the first render, then hydrates. That is what makes this a
// meta framework rather than a bundle: the greeting is in the HTML Kestrel forwards, before any
// JavaScript has executed in the browser.
// On the server, dispatch through the host's own loopback address: Node has no page to resolve a
// relative URL against. RASK_BASE_URL is injected into this process by the host, from the URL it was
// told to listen on. In the browser the default dispatcher is already right — the page and the API
// share an origin, because Kestrel owns the port.
const dispatcher = import.meta.server
  ? createDispatcher(httpTransport({ baseUrl: process.env.RASK_BASE_URL }))
  : rask

const { data: greeting } = await useAsyncData('greeting', () => dispatcher.dispatch(getGreeting({ name })))

const visits = ref<number | null>(null)
const dark = ref<boolean | null>(null)

async function visit() {
  // A command, over the same wire. `recordVisit` is POST because the C# record implements ICommand:
  // the verb comes from the type, so a command cannot be triggered by a URL or a prefetch.
  visits.value = await rask.dispatch(recordVisit({ name }))
}

onMounted(() => {
  // Browser-only, and only after mount — the media query has no answer during a Node render.
  dark.value = prefersDark()
})
</script>

<template>
  <main class="mx-auto max-w-xl p-8 font-sans">
    <h1 class="text-2xl font-semibold">Rask + Nuxt</h1>

    <p class="mt-2 text-sm text-slate-500">
      Nuxt owns this page. Kestrel owns the port, answers <code>/_rask</code> itself, and forwards
      everything else to Nuxt's own Node server on loopback.
    </p>

    <!-- Server-rendered: present in the first response, before hydration. -->
    <article data-testid="greeting" class="mt-6 rounded border p-4">
      <h2 class="font-medium">From C#, during the server render</h2>
      <p data-testid="greeting-message">{{ greeting?.message }}</p>
      <p class="text-sm text-slate-500">
        seen at <span data-testid="greeting-seen-at">{{ greeting?.seenAt }}</span>
      </p>
    </article>

    <section class="mt-6 rounded border p-4">
      <h2 class="font-medium">A command, from the browser</h2>
      <button data-testid="visit" class="rounded border px-3 py-1" @click="visit">Record a visit</button>
      <p data-testid="visits">{{ visits === null ? 'not yet' : `visits: ${visits}` }}</p>
    </section>

    <section class="mt-6 rounded border p-4">
      <h2 class="font-medium">Rask's browser layer</h2>
      <p data-testid="prefers-dark">{{ dark === null ? 'asking…' : `prefers dark: ${dark}` }}</p>
    </section>
  </main>
</template>
