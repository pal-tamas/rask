import { createDispatcher, httpTransport } from '@rask/client'
import { getGreeting } from '@rask/messages'
import { VisitButton } from './visit'

// A page that dispatches to your C# has to be dynamic, and this line is not optional. Next
// PRERENDERS server components during `next build` by default — so without it the dispatch below runs
// at build time, when no Rask host is listening, and the build fails with "Failed to parse URL from
// /_rask/…" because there is no origin either. Rendering per request is what this lane is for.
export const dynamic = 'force-dynamic'

// A React Server Component: this function runs in NODE, inside the process Kestrel supervises, and
// never in the browser. That is what makes this a meta framework rather than a bundle — the greeting
// below is in the HTML Kestrel forwards, before any JavaScript has executed on the client.
export default async function Home() {
  // Node has no page to resolve a relative URL against, so the server render dispatches through the
  // host's own address. RASK_BASE_URL is injected into this process by the host, from the URL it was
  // told to listen on — a configured value rather than one derived from an incoming request.
  const rask = createDispatcher(httpTransport({ baseUrl: process.env.RASK_BASE_URL }))
  const greeting = await rask.dispatch(getGreeting({ name: 'meta' }))

  return (
    <main className="mx-auto max-w-xl p-8 font-sans">
      <h1 className="text-2xl font-semibold">Rask + Next.js</h1>

      <p className="mt-2 text-sm text-slate-500">
        Next owns this page. Kestrel owns the port, answers <code>/_rask</code> itself, and forwards
        everything else to Next&apos;s standalone server on loopback.
      </p>

      {/* Server-rendered: present in the first response, before hydration. */}
      <article data-testid="greeting" className="mt-6 rounded border border-slate-200 p-4">
        <h2 className="font-medium">From C#, during the server render</h2>
        <p data-testid="greeting-message">{greeting.message}</p>
      </article>

      <VisitButton />
    </main>
  )
}
