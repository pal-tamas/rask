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

  return (
    <div class="flex min-h-screen flex-col bg-base-200">
      <nav class="navbar bg-base-100 shadow-sm">
        <div class="navbar-start">
          <span class="px-2 text-lg font-semibold tracking-tight">Rask + Solid</span>
        </div>
        <div class="navbar-end">
          <a class="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
        </div>
      </nav>

      <main class="hero grow py-16">
        <div class="hero-content text-center">
          <div class="max-w-md">
            <h1 class="text-4xl font-bold">Rask + Solid</h1>
            <p class="py-4 text-base-content/70">
              One query and one command, over your C# records.
            </p>

            <div class="card bg-base-100 w-full max-w-md shadow-sm">
              <div class="card-body gap-4 text-left">
                <label class="fieldset">
                  <span class="fieldset-legend">Name</span>
                  <input
                    class="input w-full"
                    value={name()}
                    onInput={(event) => setName(event.currentTarget.value)}
                  />
                </label>

                <Show when={greeting.loading}>
                  <span class="loading loading-spinner loading-sm" aria-label="Loading" />
                </Show>
                <Show when={greeting.error}>
                  {(error) => (
                    <div role="alert" class="alert alert-error">
                      <span>{String(error())}</span>
                    </div>
                  )}
                </Show>

                <Show when={greeting()}>
                  {(data) => (
                    <>
                      <p>{data().message}</p>
                      {/* seenAt is a real Date, revived because the C# type said it was an instant. */}
                      <p class="text-sm text-base-content/70">
                        Server time:{' '}
                        {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(data().seenAt)}
                      </p>
                      <div class="stat p-0">
                        <div class="stat-title">Visits</div>
                        <div class="stat-value text-2xl">{data().visits}</div>
                      </div>
                    </>
                  )}
                </Show>

                <div class="card-actions justify-end">
                  <button class="btn btn-primary" onClick={visit} disabled={busy()}>
                    Record a visit
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </main>

      <footer class="footer footer-center bg-base-100 p-4 text-base-content/70">
        <aside><p>Built with Rask.</p></aside>
      </footer>
    </div>
  )
}
