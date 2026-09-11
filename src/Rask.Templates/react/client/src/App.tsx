import { useCallback, useEffect, useState } from 'react'
import { rask } from './rask/client'
import { getGreeting, recordVisit } from './rask/messages'
import type { Greeting } from './rask/contracts'

export default function App() {
  const [name, setName] = useState('world')
  const [greeting, setGreeting] = useState<Greeting | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // rask.dispatch and nothing else. The message carries its own result type, so `greeting` is
  // a Greeting with no cast and no wire name spelled out here — renaming a property in the C#
  // record breaks this at build time rather than on the wire.
  const load = useCallback(async (signal?: AbortSignal) => {
    setError(null)
    try {
      setGreeting(await rask.dispatch(getGreeting({ name }), { signal }))
    } catch (e) {
      // An aborted request is the previous keystroke being superseded, not a failure.
      if (!signal?.aborted) setError(e instanceof Error ? e.message : String(e))
    }
  }, [name])

  // Refetch as the name changes, and abort the request in flight so a slow earlier one
  // cannot land after a later one and show the wrong answer.
  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const visit = async () => {
    setBusy(true)
    try {
      await rask.dispatch(recordVisit({ name }))
      await load()
    } finally {
      setBusy(false)
    }
  }

  // daisyUI's own class names, and the same navbar / hero / card / footer skeleton every
  // other `rask new` template draws — so a project looks the same whichever front end it
  // was scaffolded with. Spelled out in full: Tailwind emits a class only where it can see
  // the name, so a name built by concatenation styles nothing.
  return (
    <div className="flex min-h-screen flex-col bg-base-200">
      <nav className="navbar bg-base-100 shadow-sm">
        <div className="navbar-start">
          <span className="px-2 text-lg font-semibold tracking-tight">Rask + React</span>
        </div>
        <div className="navbar-end">
          <a className="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
        </div>
      </nav>

      <main className="hero grow py-16">
        <div className="hero-content text-center">
          <div className="max-w-md">
            <h1 className="text-4xl font-bold">Rask + React</h1>
            <p className="py-4 text-base-content/70">
              One query and one command, over your C# records.
            </p>

            <div className="card bg-base-100 w-full max-w-md shadow-sm">
              <div className="card-body gap-4 text-left">
                <label className="fieldset">
                  <span className="fieldset-legend">Name</span>
                  <input
                    className="input w-full"
                    value={name}
                    onChange={(event) => setName(event.target.value)}
                  />
                </label>

                {!greeting && !error && (
                  <span className="loading loading-spinner loading-sm" aria-label="Loading" />
                )}
                {error && (
                  <div role="alert" className="alert alert-error"><span>{error}</span></div>
                )}

                {greeting && (
                  <>
                    <p>{greeting.message}</p>
                    {/* seenAt is a real Date, revived because the C# type said it was an
                        instant — not because the string looked like one. Formatting is the
                        browser's job: `undefined` means the visitor's own locale, and their
                        own time zone. */}
                    <p className="text-sm text-base-content/70">
                      Server time:{' '}
                      {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
                    </p>
                    <div className="stat p-0">
                      <div className="stat-title">Visits</div>
                      <div className="stat-value text-2xl">{greeting.visits}</div>
                    </div>
                  </>
                )}

                <div className="card-actions justify-end">
                  <button className="btn btn-primary" onClick={visit} disabled={busy}>
                    Record a visit
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </main>

      <footer className="footer footer-center bg-base-100 p-4 text-base-content/70">
        <aside><p>Built with Rask.</p></aside>
      </footer>
    </div>
  )
}
