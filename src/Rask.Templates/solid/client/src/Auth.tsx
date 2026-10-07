import { Show, createSignal } from 'solid-js'
import { login, register, type AuthFailure } from './rask/browser/auth'

const input =
  'w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950'

/**
 * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
 * client is typed, so there is no status code to read and no shape to guess, and the cookie
 * it sets is HttpOnly — this page never sees a token.
 */
export default function Auth(props: { mode: 'login' | 'register' }) {
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
    <main class="grid min-h-screen place-items-center px-4">
      <div class="flex w-full max-w-sm flex-col items-center gap-6">
        <h1 class="text-2xl font-bold">{registering() ? 'Create an account' : 'Sign in'}</h1>

        <form
          class="flex w-full flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 shadow-xs dark:border-zinc-800 dark:bg-zinc-900"
          onSubmit={submit}
        >
          <Show when={failure()}>
            {(error) => (
              <div
                role="alert"
                class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
              >
                {error().message ?? error().error}
              </div>
            )}
          </Show>

          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">Email</span>
            <input
              class={input}
              type="email"
              autocomplete="username"
              required
              value={email()}
              onInput={(event) => setEmail(event.currentTarget.value)}
            />
          </label>

          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">Password</span>
            <input
              class={input}
              type="password"
              autocomplete={registering() ? 'new-password' : 'current-password'}
              required
              value={password()}
              onInput={(event) => setPassword(event.currentTarget.value)}
            />
          </label>

          <button
            class="w-full rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
            type="submit"
            disabled={busy()}
          >
            {registering() ? 'Create account' : 'Sign in'}
          </button>

          <p class="text-sm">
            <a
              class="underline-offset-4 hover:underline"
              href={registering() ? '/login' : '/register'}
            >
              {registering() ? 'Already have an account?' : 'No account yet?'}
            </a>
          </p>
        </form>
      </div>
    </main>
  )
}
