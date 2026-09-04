'use client'

import { useState } from 'react'
import { rask } from '@rask/client'
import { recordVisit } from '@rask/messages'
import { prefersDark } from '@rask/browser/mediaQuery'

// The browser half. `'use client'` is Next's way of saying this component hydrates and runs in the
// page — everything above it in page.tsx ran in Node and never ships.
export function VisitButton() {
  const [visits, setVisits] = useState<number | null>(null)
  const [dark, setDark] = useState<boolean | null>(null)

  async function visit() {
    // A command, over the same wire the server render used. POST, because the C# record implements
    // ICommand — the verb comes from the type, so a command cannot be triggered by a URL or a prefetch.
    setVisits(await rask.dispatch(recordVisit({ name: 'meta' })))

    // Rask's typed browser layer, the same TypeScript the Server and WASM clients run. Called from an
    // event handler rather than at module scope: this file is still loaded by Node during the server
    // pass, and a module that touched `window` on import would crash the render.
    setDark(prefersDark())
  }

  return (
    <section style={{ border: '1px solid #e2e8f0', padding: '1rem', marginTop: '1.5rem' }}>
      <h2>A command, from the browser</h2>
      <button data-testid="visit" onClick={visit}>Record a visit</button>
      <p data-testid="visits">{visits === null ? 'not yet' : `visits: ${visits}`}</p>
      <p data-testid="prefers-dark">{dark === null ? 'asking…' : `prefers dark: ${dark}`}</p>
    </section>
  )
}
