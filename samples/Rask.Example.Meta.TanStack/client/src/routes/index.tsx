import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { createServerFn } from '@tanstack/react-start'
import { createDispatcher, httpTransport, rask } from '@rask/client'
import { getGreeting, recordVisit } from '@rask/messages'

// Rask's typed browser layer, the same TypeScript the Server and WASM clients run. Imported at module
// scope on purpose: this file is loaded by NODE during the server render before any browser sees it,
// so a module that touched `window` on import would crash the page rather than degrade.
import { prefersDark } from '@rask/browser/mediaQuery'

// A server function runs in NODE, inside the process Kestrel supervises. Node has no page to resolve
// a relative URL against, so it dispatches through the host's own address — RASK_BASE_URL, injected
// into this process by the host from the URL it was told to listen on.
const fetchGreeting = createServerFn().handler(async () => {
  const dispatcher = createDispatcher(httpTransport({ baseUrl: process.env.RASK_BASE_URL }))
  return dispatcher.dispatch(getGreeting({ name: 'meta' }))
})

export const Route = createFileRoute('/')({
  component: App,
  // Loaded before the route renders, on the server for the first request — so the greeting is in the
  // HTML Kestrel forwards, before any script has executed in the browser.
  loader: () => fetchGreeting(),
})

function App() {
  const greeting = Route.useLoaderData()
  const [visits, setVisits] = useState<number | null>(null)
  const [dark, setDark] = useState<boolean | null>(null)

  async function visit() {
    // A command over the same wire, from the browser: POST, because the C# record implements
    // ICommand and the verb comes from the type rather than from this call site.
    setVisits(await rask.dispatch(recordVisit({ name: 'meta' })))
    setDark(prefersDark())
  }

  return (
    <main className="mx-auto max-w-xl p-8 font-sans">
      <h1 className="text-2xl font-semibold">Rask + TanStack Start</h1>

      <p className="mt-2 text-sm text-slate-500">
        TanStack Start owns this page. Kestrel owns the port, answers <code>/_rask</code> itself, and
        forwards everything else to its Nitro server on loopback.
      </p>

      <article data-testid="greeting" className="mt-6 rounded border border-slate-200 p-4">
        <h2 className="font-medium">From C#, during the server render</h2>
        <p data-testid="greeting-message">{greeting.message}</p>
      </article>

      <section className="mt-6 rounded border border-slate-200 p-4">
        <h2 className="font-medium">A command, from the browser</h2>
        <button className="rounded border border-slate-300 px-3 py-1 hover:bg-slate-50" data-testid="visit" onClick={visit}>Record a visit</button>
        <p className="mt-2 text-sm" data-testid="visits">{visits === null ? 'not yet' : `visits: ${visits}`}</p>
        <p className="text-sm" data-testid="prefers-dark">{dark === null ? 'asking…' : `prefers dark: ${dark}`}</p>
      </section>
    </main>
  )
}
