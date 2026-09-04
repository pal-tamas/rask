import { createSignal, Show } from 'solid-js'
import { createAsync, query } from '@solidjs/router'
import { createDispatcher, httpTransport, rask } from '@rask/client'
import { getGreeting, recordVisit } from '@rask/messages'

// Rask's typed browser layer, the same TypeScript the Server and WASM clients run. Imported at module
// scope on purpose: this file is loaded by NODE during the server render before any browser sees it,
// so a module that touched `window` on import would crash the page rather than degrade.
import { prefersDark } from '@rask/browser/mediaQuery'

// A `query` runs on the SERVER for the first render and is serialized into the page. Node has no page
// to resolve a relative URL against, so it dispatches through the host's own address —
// RASK_BASE_URL, injected into this process by the host from the URL it was told to listen on.
const greeting = query(async () => {
  'use server'
  const dispatcher = createDispatcher(httpTransport({ baseUrl: process.env.RASK_BASE_URL }))
  return dispatcher.dispatch(getGreeting({ name: 'meta' }))
}, 'greeting')

export const route = { preload: () => greeting() }

export default function Home() {
  const data = createAsync(() => greeting())
  const [visits, setVisits] = createSignal<number | null>(null)
  const [dark, setDark] = createSignal<boolean | null>(null)

  async function visit() {
    // A command over the same wire, from the browser: POST, because the C# record implements
    // ICommand and the verb comes from the type rather than from this call site.
    setVisits(await rask.dispatch(recordVisit({ name: 'meta' })))
    setDark(prefersDark())
  }

  return (
    <main class="mx-auto max-w-xl p-8 font-sans">
      <h1 class="text-2xl font-semibold">Rask + SolidStart</h1>

      <p class="mt-2 text-sm text-slate-500">
        SolidStart owns this page. Kestrel owns the port, answers <code>/_rask</code> itself, and
        forwards everything else to SolidStart's Nitro server on loopback.
      </p>

      <article data-testid="greeting" class="mt-6 rounded border border-slate-200 p-4">
        <h2 class="font-medium">From C#, during the server render</h2>
        <p data-testid="greeting-message">
          <Show when={data()}>{(g) => g().message}</Show>
        </p>
      </article>

      <section class="mt-6 rounded border border-slate-200 p-4">
        <h2 class="font-medium">A command, from the browser</h2>
        <button class="rounded border border-slate-300 px-3 py-1 hover:bg-slate-50" data-testid="visit" onClick={visit}>Record a visit</button>
        <p class="mt-2 text-sm" data-testid="visits">{visits() === null ? 'not yet' : `visits: ${visits()}`}</p>
        <p class="text-sm" data-testid="prefers-dark">{dark() === null ? 'asking…' : `prefers dark: ${dark()}`}</p>
      </section>
    </main>
  )
}
