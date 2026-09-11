import { Show, createSignal } from 'solid-js'

import { login, register, type AuthFailure } from '@rask/browser/auth'

/**
 * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
 * client is typed, so there is no status code to read and no shape to guess, and the cookie it
 * sets is HttpOnly — this page never sees a token.
 */
export default function AuthForm(props: { mode: 'login' | 'register' }) {
  const registering = () => props.mode === 'register'
  const [email, setEmail] = createSignal('')
  const [password, setPassword] = createSignal('')
  const [failure, setFailure] = createSignal<AuthFailure | null>(null)
  const [busy, setBusy] = createSignal(false)

  async function submit(event: Event) {
    event.preventDefault()
    setBusy(true)
    setFailure(null)

    const credentials = { email: email(), password: password() }
    const result = registering() ? await register(credentials) : await login(credentials)

    setBusy(false)
    if (result.ok) window.location.assign('/')
    else setFailure(result.failure)
  }

  return (
    <main class="hero min-h-screen bg-base-200">
      <div class="hero-content w-full max-w-sm flex-col">
        <h1 class="text-2xl font-bold">
          {registering() ? 'Create an account' : 'Sign in'}
        </h1>

        <form class="card bg-base-100 w-full shadow-sm" onSubmit={submit}>
          <div class="card-body gap-4">
            <Show when={failure()}>
              {(problem) => (
                <div role="alert" class="alert alert-error">
                  <span>{problem().message ?? problem().error}</span>
                </div>
              )}
            </Show>

            <label class="fieldset">
              <span class="fieldset-legend">Email</span>
              <input
                class="input w-full"
                type="email"
                autocomplete="username"
                required
                value={email()}
                onInput={(event) => setEmail(event.currentTarget.value)}
              />
            </label>

            <label class="fieldset">
              <span class="fieldset-legend">Password</span>
              <input
                class="input w-full"
                type="password"
                autocomplete={registering() ? 'new-password' : 'current-password'}
                required
                value={password()}
                onInput={(event) => setPassword(event.currentTarget.value)}
              />
            </label>

            <div class="card-actions">
              <button class="btn btn-primary btn-block" type="submit" disabled={busy()}>
                {registering() ? 'Create account' : 'Sign in'}
              </button>
            </div>

            <p class="text-sm">
              <a class="link link-primary" href={registering() ? '/login' : '/register'}>
                {registering() ? 'Already have an account?' : 'No account yet?'}
              </a>
            </p>
          </div>
        </form>
      </div>
    </main>
  )
}
