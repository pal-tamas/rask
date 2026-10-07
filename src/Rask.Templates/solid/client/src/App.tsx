import { Show, createResource, createSignal } from 'solid-js'
import { rask } from './rask/client'
import { getGreeting, recordVisit } from './rask/messages'

export default function App() {
  const [name, setName] = createSignal('world')
  const [busy, setBusy] = createSignal(false)

  // createResource takes the signal as its SOURCE, and that is not a formality: it is what
  // re-runs the fetch when the name changes. Reading name() inside the fetcher alone would
  // read it once, at setup, and never again.
  const [greeting, { refetch }] = createResource(name, (value) =>
    rask.dispatch(getGreeting({ name: value })),
  )

  const visit = async () => {
    setBusy(true)
    try {
      await rask.dispatch(recordVisit({ name: name() }))
      await refetch()
    } finally {
      setBusy(false)
    }
  }

  // Plain Tailwind utilities, spelled out in full: Tailwind emits a class only where it can
  // see the name, so a name built by concatenation styles nothing.
  return (
    <div class="flex min-h-screen flex-col">
      <nav class="flex items-center justify-between border-b border-zinc-200 bg-white px-4 py-3 dark:border-zinc-800 dark:bg-zinc-900">
        <span class="text-lg font-semibold tracking-tight">Rask + Solid</span>
        <a class="text-sm underline-offset-4 hover:underline" href="https://rask.sh/docs">
          Docs
        </a>
      </nav>

      <main class="grid grow place-items-center px-4 py-16">
        <div class="w-full max-w-md text-center">
          <h1 class="text-4xl font-bold">Rask + Solid</h1>
          <p class="py-4 text-zinc-500 dark:text-zinc-400">
            One query and one command, over your C# records.
          </p>

          <div class="flex flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 text-left shadow-xs dark:border-zinc-800 dark:bg-zinc-900">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">Name</span>
              <input
                class="w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950"
                value={name()}
                onInput={(event) => setName(event.currentTarget.value)}
              />
            </label>

            <Show when={greeting.loading}>
              <span
                class="size-4 animate-spin rounded-full border-2 border-zinc-300 border-t-zinc-900 dark:border-zinc-700 dark:border-t-zinc-100"
                role="status"
                aria-label="Loading"
              />
            </Show>
            <Show when={greeting.error}>
              {(error) => (
                <div
                  role="alert"
                  class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
                >
                  {String(error())}
                </div>
              )}
            </Show>

            <Show when={greeting()}>
              {(data) => (
                <>
                  <p>{data().message}</p>
                  {/* seenAt is a real Date, revived because the C# type said it was an instant. */}
                  <p class="text-sm text-zinc-500 dark:text-zinc-400">
                    Server time:{' '}
                    {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(
                      data().seenAt,
                    )}
                  </p>
                  <div>
                    <div class="text-sm text-zinc-500 dark:text-zinc-400">Visits</div>
                    <div class="text-2xl font-semibold tabular-nums">{data().visits}</div>
                  </div>
                </>
              )}
            </Show>

            <div class="flex justify-end">
              <button
                class="rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
                onClick={visit}
                disabled={busy()}
              >
                Record a visit
              </button>
            </div>
          </div>
        </div>
      </main>

      <footer class="border-t border-zinc-200 bg-white p-4 text-center text-sm text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-400">
        Built with Rask.
      </footer>
    </div>
  )
}
