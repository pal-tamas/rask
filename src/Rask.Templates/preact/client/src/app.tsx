import { useCallback, useEffect, useState } from 'preact/hooks'
import { rask } from './rask/client'
import { getGreeting, recordVisit } from './rask/messages'
import type { Greeting } from './rask/contracts'

export function App() {
  const [name, setName] = useState('world')
  const [greeting, setGreeting] = useState<Greeting | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // rask.dispatch and nothing else. The message carries its own result type, so `greeting` is
  // a Greeting with no cast and no wire name spelled out here — renaming a property in the C#
  // record breaks this at build time rather than on the wire.
  const load = useCallback(
    async (signal?: AbortSignal) => {
      setError(null)
      try {
        setGreeting(await rask.dispatch(getGreeting({ name }), { signal }))
      } catch (e) {
        // An aborted request is the previous keystroke being superseded, not a failure.
        if (!signal?.aborted) setError(e instanceof Error ? e.message : String(e))
      }
    },
    [name],
  )

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

  // Plain Tailwind utilities, spelled out in full: Tailwind emits a class only where it can
  // see the name, so a name built by concatenation styles nothing.
  return (
    <div className="flex min-h-screen flex-col">
      <nav className="flex items-center justify-between border-b border-zinc-200 bg-white px-4 py-3 dark:border-zinc-800 dark:bg-zinc-900">
        <span className="text-lg font-semibold tracking-tight">Rask + Preact</span>
        <a className="text-sm underline-offset-4 hover:underline" href="https://rask.sh/docs">
          Docs
        </a>
      </nav>

      <main className="grid grow place-items-center px-4 py-16">
        <div className="w-full max-w-md text-center">
          <h1 className="text-4xl font-bold">Rask + Preact</h1>
          <p className="py-4 text-zinc-500 dark:text-zinc-400">
            One query and one command, over your C# records.
          </p>

          <div className="flex flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 text-left shadow-xs dark:border-zinc-800 dark:bg-zinc-900">
            <label className="flex flex-col gap-1">
              <span className="text-sm font-medium">Name</span>
              <input
                className="w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950"
                value={name}
                onInput={(event) => setName(event.currentTarget.value)}
              />
            </label>

            {!greeting && !error && (
              <span
                className="size-4 animate-spin rounded-full border-2 border-zinc-300 border-t-zinc-900 dark:border-zinc-700 dark:border-t-zinc-100"
                role="status"
                aria-label="Loading"
              />
            )}
            {error && (
              <div
                role="alert"
                className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
              >
                {error}
              </div>
            )}

            {greeting && (
              <>
                <p>{greeting.message}</p>
                {/* seenAt is a real Date, revived because the C# type said it was an
                    instant — not because the string looked like one. Formatting is the
                    browser's job: `undefined` means the visitor's own locale, and their
                    own time zone. */}
                <p className="text-sm text-zinc-500 dark:text-zinc-400">
                  Server time:{' '}
                  {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(
                    greeting.seenAt,
                  )}
                </p>
                <div>
                  <div className="text-sm text-zinc-500 dark:text-zinc-400">Visits</div>
                  <div className="text-2xl font-semibold tabular-nums">{greeting.visits}</div>
                </div>
              </>
            )}

            <div className="flex justify-end">
              <button
                className="rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
                onClick={visit}
                disabled={busy}
              >
                Record a visit
              </button>
            </div>
          </div>
        </div>
      </main>

      <footer className="border-t border-zinc-200 bg-white p-4 text-center text-sm text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-400">
        Built with Rask.
      </footer>
    </div>
  )
}
