<script lang="ts">
  // `@rask/messages` is generated from the C# records in Features/Hello and `@rask/client` carries
  // them. Rename a property on the server and this file stops compiling — no schema to keep in sync,
  // no URL written at a call site.
  import { rask } from '@rask/client'
  import { recordVisit } from '@rask/messages'

  // Rask's typed browser layer, the same TypeScript the Server and WASM clients run. Imported at
  // module scope deliberately: this file is loaded by NODE during the server render before it is ever
  // loaded by a browser, so a module that touched `window` on import would crash the page.
  import { prefersDark } from '@rask/browser/mediaQuery'

  // The alias SvelteKit generates from kit.alias — and $lib, which it generates too. Both resolving
  // in one file is the thing that broke while every structural test stayed green.
  import { greetingLabel } from '$lib'

  let { data } = $props()

  let visits: number | null = $state(null)
  let dark: boolean | null = $state(null)

  async function visit() {
    // A command over the same wire: POST, because the C# record implements ICommand.
    visits = await rask.dispatch(recordVisit({ name: 'meta' }))
    dark = prefersDark()
  }
</script>

<main class="mx-auto max-w-xl p-8 font-sans">
  <h1 class="text-2xl font-semibold">Rask + SvelteKit</h1>

  <p class="mt-2 text-sm text-slate-500">
    SvelteKit owns this page. Kestrel owns the port, answers <code>/_rask</code> itself, and forwards
    everything else to SvelteKit's adapter-node server on loopback.
  </p>

  <article data-testid="greeting" class="mt-6 rounded border border-slate-200 p-4">
    <h2 class="font-medium">{greetingLabel}</h2>
    <p data-testid="greeting-message">{data.greeting.message}</p>
  </article>

  <section class="mt-6 rounded border border-slate-200 p-4">
    <h2 class="font-medium">A command, from the browser</h2>
    <button class="rounded border border-slate-300 px-3 py-1 hover:bg-slate-50" data-testid="visit" onclick={visit}>Record a visit</button>
    <p class="mt-2 text-sm" data-testid="visits">{visits === null ? 'not yet' : `visits: ${visits}`}</p>
    <p class="text-sm" data-testid="prefers-dark">{dark === null ? 'asking…' : `prefers dark: ${dark}`}</p>
  </section>
</main>
