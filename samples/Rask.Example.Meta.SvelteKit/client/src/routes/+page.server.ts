import { createDispatcher, httpTransport } from '@rask/client'
import { getGreeting } from '@rask/messages'
import type { PageServerLoad } from './$types'

// A +page.server.ts load runs in NODE, inside the process Kestrel supervises, and never in the
// browser — so the greeting is in the HTML Kestrel forwards, before any script has executed.
//
// Node has no page to resolve a relative URL against, so this dispatches through the host's own
// address. RASK_BASE_URL is injected into this process by the host, from the URL it was told to
// listen on: a configured value rather than one derived from an incoming request.
export const load: PageServerLoad = async () => {
  const rask = createDispatcher(httpTransport({ baseUrl: process.env.RASK_BASE_URL }))

  return { greeting: await rask.dispatch(getGreeting({ name: 'meta' })) }
}
