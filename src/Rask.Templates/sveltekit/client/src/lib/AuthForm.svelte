<script lang="ts">
  import { login, register, type AuthFailure } from '@rask/browser/auth'

  /*
    Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
    client is typed, so there is no status code to read and no shape to guess, and the cookie
    it sets is HttpOnly — this page never sees a token.
  */
  let { mode }: { mode: 'login' | 'register' } = $props()

  const registering = $derived(mode === 'register')
  let email = $state('')
  let password = $state('')
  let failure = $state<AuthFailure | null>(null)
  let busy = $state(false)

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

<main class="hero min-h-screen bg-base-200">
  <div class="hero-content w-full max-w-sm flex-col">
    <h1 class="text-2xl font-bold">
      {registering ? 'Create an account' : 'Sign in'}
    </h1>

    <form class="card bg-base-100 w-full shadow-sm" onsubmit={submit}>
      <div class="card-body gap-4">
        {#if failure}
          <div role="alert" class="alert alert-error">
            <span>{failure.message ?? failure.error}</span>
          </div>
        {/if}

        <label class="fieldset">
          <span class="fieldset-legend">Email</span>
          <input
            class="input w-full"
            type="email"
            autocomplete="username"
            required
            bind:value={email}
          />
        </label>

        <label class="fieldset">
          <span class="fieldset-legend">Password</span>
          <input
            class="input w-full"
            type="password"
            autocomplete={registering ? 'new-password' : 'current-password'}
            required
            bind:value={password}
          />
        </label>

        <div class="card-actions">
          <button class="btn btn-primary btn-block" type="submit" disabled={busy}>
            {registering ? 'Create account' : 'Sign in'}
          </button>
        </div>

        <p class="text-sm">
          <a class="link link-primary" href={registering ? '/login' : '/register'}>
            {registering ? 'Already have an account?' : 'No account yet?'}
          </a>
        </p>
      </div>
    </form>
  </div>
</main>
