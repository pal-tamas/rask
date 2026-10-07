<script lang="ts">
  import { login, register, type AuthFailure } from '../rask/browser/auth'

  /*
    Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
    client is typed, so there is no status code to read and no shape to guess, and the
    cookie it sets is HttpOnly — this page never sees a token.
  */
  let { mode }: { mode: 'login' | 'register' } = $props()

  const registering = $derived(mode === 'register')
  let email = $state('')
  let password = $state('')
  let failure = $state<AuthFailure | null>(null)
  let busy = $state(false)

  const input =
    'w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950'

  async function submit(event: SubmitEvent) {
    event.preventDefault()
    busy = true
    failure = null

    const credentials = { email, password }
    const result = registering ? await register(credentials) : await login(credentials)

    busy = false
    if (result.ok) window.location.assign('/')
    else failure = result.failure
  }
</script>

<main class="grid min-h-screen place-items-center px-4">
  <div class="flex w-full max-w-sm flex-col items-center gap-6">
    <h1 class="text-2xl font-bold">
      {registering ? 'Create an account' : 'Sign in'}
    </h1>

    <form class="flex w-full flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 shadow-xs dark:border-zinc-800 dark:bg-zinc-900" onsubmit={submit}>
      {#if failure}
        <div role="alert" class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200">{failure.message ?? failure.error}</div>
      {/if}

      <label class="flex flex-col gap-1">
        <span class="text-sm font-medium">Email</span>
        <input class={input} type="email" autocomplete="username" required bind:value={email} />
      </label>

      <label class="flex flex-col gap-1">
        <span class="text-sm font-medium">Password</span>
        <input
          class={input}
          type="password"
          autocomplete={registering ? 'new-password' : 'current-password'}
          required
          bind:value={password}
        />
      </label>

      <button class="w-full rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200" type="submit" disabled={busy}>
        {registering ? 'Create account' : 'Sign in'}
      </button>

      <p class="text-sm">
        <a class="underline-offset-4 hover:underline" href={registering ? '/login' : '/register'}>
          {registering ? 'Already have an account?' : 'No account yet?'}
        </a>
      </p>
    </form>
  </div>
</main>
