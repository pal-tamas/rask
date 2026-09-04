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
    <main style={{ maxWidth: '40rem', margin: '0 auto', padding: '2rem', fontFamily: 'system-ui' }}>
      <h1>Rask + Next.js</h1>

      <p style={{ color: '#64748b', fontSize: '0.875rem' }}>
        Next owns this page. Kestrel owns the port, answers <code>/_rask</code> itself, and forwards
        everything else to Next&apos;s standalone server on loopback.
      </p>

      {/* Server-rendered: present in the first response, before hydration. */}
      <article data-testid="greeting" style={{ border: '1px solid #e2e8f0', padding: '1rem', marginTop: '1.5rem' }}>
        <h2>From C#, during the server render</h2>
        <p data-testid="greeting-message">{greeting.message}</p>
      </article>

      <VisitButton />
    </main>
  )
}
