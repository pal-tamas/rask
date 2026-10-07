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

<!-- Plain Tailwind utilities, spelled out in full: Tailwind emits a class only where it can see
     the name, so a name built by concatenation styles nothing. -->
<div class="flex min-h-screen flex-col">
  <nav class="flex items-center justify-between border-b border-zinc-200 bg-white px-4 py-3 dark:border-zinc-800 dark:bg-zinc-900">
    <span class="text-lg font-semibold tracking-tight">Rask + Svelte</span>
    <a class="text-sm underline-offset-4 hover:underline" href="https://rask.sh/docs">Docs</a>
  </nav>

  <main class="grid grow place-items-center px-4 py-16">
    <div class="w-full max-w-md text-center">
      <h1 class="text-4xl font-bold">Rask + Svelte</h1>
      <p class="py-4 text-zinc-500 dark:text-zinc-400">
        One query and one command, over your C# records.
      </p>

      <div class="flex flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 text-left shadow-xs dark:border-zinc-800 dark:bg-zinc-900">
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">Name</span>
          <input class="w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950" bind:value={name} />
        </label>

        {#if !greeting && !error}
          <span class="size-4 animate-spin rounded-full border-2 border-zinc-300 border-t-zinc-900 dark:border-zinc-700 dark:border-t-zinc-100" role="status" aria-label="Loading"></span>
        {:else if error}
          <div role="alert" class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200">{error}</div>
        {/if}

        {#if greeting}
          <p>{greeting.message}</p>
          <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
          <p class="text-sm text-zinc-500 dark:text-zinc-400">
            Server time:
            {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
          </p>
          <div>
            <div class="text-sm text-zinc-500 dark:text-zinc-400">Visits</div>
            <div class="text-2xl font-semibold tabular-nums">{greeting.visits}</div>
          </div>
        {/if}

        <div class="flex justify-end">
          <button class="rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200" onclick={visit} disabled={busy}>Record a visit</button>
        </div>
      </div>
    </div>
  </main>

  <footer class="border-t border-zinc-200 bg-white p-4 text-center text-sm text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-400">Built with Rask.</footer>
</div>
