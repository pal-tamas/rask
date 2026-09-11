import { useCallback, useEffect, useState } from 'preact/hooks'
import { rask } from './rask/client'
import { getGreeting, recordVisit } from './rask/messages'
import type { Greeting } from './rask/contracts'

export function App() {
  const [name, setName] = useState('world')
  const [greeting, setGreeting] = useState<Greeting | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // The message carries its own result type, so `greeting` is a Greeting with no cast and no
  // wire name spelled out here.
  const load = useCallback(
    async (signal?: AbortSignal) => {
      setError(null)
      try {
        setGreeting(await rask.dispatch(getGreeting({ name }), { signal }))
      } catch (e) {
        if (!signal?.aborted) setError(e instanceof Error ? e.message : String(e))
      }
    },
    [name],
  )

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

  return (
    <div className="flex min-h-screen flex-col bg-base-200">
      <nav className="navbar bg-base-100 shadow-sm">
        <div className="navbar-start">
          <span className="px-2 text-lg font-semibold tracking-tight">Rask + Preact</span>
        </div>
        <div className="navbar-end">
          <a className="link link-hover link-primary" href="https://rask.sh/docs">
            Docs
          </a>
        </div>
      </nav>

      <main className="hero grow py-16">
        <div className="hero-content text-center">
          <div className="max-w-md">
            <h1 className="text-4xl font-bold">Rask + Preact</h1>
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
                    onInput={(event) => setName((event.target as HTMLInputElement).value)}
                  />
                </label>

                {!greeting && !error && (
                  <span className="loading loading-spinner loading-sm" aria-label="Loading" />
                )}
                {error && (
                  <div role="alert" className="alert alert-error">
                    <span>{error}</span>
                  </div>
                )}

                {greeting && (
                  <>
                    <p>{greeting.message}</p>
                    {/* seenAt is a real Date, revived because the C# type said it was an instant. */}
                    <p className="text-sm text-base-content/70">
                      Server time:{' '}
                      {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(
                        greeting.seenAt,
                      )}
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
        <aside>
          <p>Built with Rask.</p>
        </aside>
      </footer>
    </div>
  )
}
