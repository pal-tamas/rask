<script lang="ts">
  import { rask } from '../rask/client'
  import { getGreeting, recordVisit } from '../rask/messages'
  import type { Greeting } from '../rask/contracts'

  let name = $state('world')
  let greeting = $state<Greeting | null>(null)
  let error = $state<string | null>(null)
  let busy = $state(false)

  let inFlight: AbortController | null = null

  async function load() {
    inFlight?.abort()
    const controller = new AbortController()
    inFlight = controller
    error = null

    try {
      greeting = await rask.dispatch(getGreeting({ name }), { signal: controller.signal })
    } catch (e) {
      if (!controller.signal.aborted) error = e instanceof Error ? e.message : String(e)
    }
  }

  // $effect re-runs when `name` changes, which is what makes this a live query rather than a
  // one-off read at setup. The abort above is what stops a slow earlier request landing after
  // a later one and showing the wrong answer.
  $effect(() => {
    name
    void load()
  })

  async function visit() {
    busy = true
    try {
      await rask.dispatch(recordVisit({ name }))
      await load()
    } finally {
      busy = false
    }
  }
</script>

<div class="flex min-h-screen flex-col bg-base-200">
  <nav class="navbar bg-base-100 shadow-sm">
    <div class="navbar-start">
      <span class="px-2 text-lg font-semibold tracking-tight">Rask + Svelte</span>
    </div>
    <div class="navbar-end">
      <a class="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
    </div>
  </nav>

  <main class="hero grow py-16">
    <div class="hero-content text-center">
      <div class="max-w-md">
        <h1 class="text-4xl font-bold">Rask + Svelte</h1>
        <p class="py-4 text-base-content/70">
          One query and one command, over your C# records.
        </p>

        <div class="card bg-base-100 w-full max-w-md shadow-sm">
          <div class="card-body gap-4 text-left">
            <label class="fieldset">
              <span class="fieldset-legend">Name</span>
              <input class="input w-full" bind:value={name} />
            </label>

            {#if !greeting && !error}
              <span class="loading loading-spinner loading-sm" aria-label="Loading"></span>
            {:else if error}
              <div role="alert" class="alert alert-error"><span>{error}</span></div>
            {:else if greeting}
              <p>{greeting.message}</p>
              <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
              <p class="text-sm text-base-content/70">
                Server time:
                {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
              </p>
              <div class="stat p-0">
                <div class="stat-title">Visits</div>
                <div class="stat-value text-2xl">{greeting.visits}</div>
              </div>
            {/if}

            <div class="card-actions justify-end">
              <button class="btn btn-primary" onclick={visit} disabled={busy}>
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
