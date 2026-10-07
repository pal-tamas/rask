import { useState } from 'preact/hooks'
import { login, register, type AuthFailure } from './rask/browser/auth'

const input =
  'w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950'

/**
 * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth.
 *
 * The client is generated into rask/browser and typed: `login` answers
 * `{ok: true, user}` or `{ok: false, failure}`, so there is no status code to read and no
 * shape to guess. The cookie it sets is HttpOnly — this page never sees a token, so there
 * is nothing here to keep in browser storage.
 */
export function Auth({ mode }: { mode: 'login' | 'register' }) {
  const registering = mode === 'register'
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [failure, setFailure] = useState<AuthFailure | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: Event) {
    event.preventDefault()
    setBusy(true)
    setFailure(null)

    const result = registering
      ? await register({ email, password })
      : await login({ email, password })

    setBusy(false)
    if (result.ok) window.location.assign('/')
    else setFailure(result.failure)
  }

  return (
    <main className="grid min-h-screen place-items-center px-4">
      <div className="flex w-full max-w-sm flex-col items-center gap-6">
        <h1 className="text-2xl font-bold">{registering ? 'Create an account' : 'Sign in'}</h1>

        <form
          className="flex w-full flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 shadow-xs dark:border-zinc-800 dark:bg-zinc-900"
          onSubmit={submit}
        >
          {failure && (
            <div
              role="alert"
              className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
            >
              {failure.message ?? failure.error}
            </div>
          )}

          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium">Email</span>
            <input
              className={input}
              type="email"
              autoComplete="username"
              required
              value={email}
              onInput={(event) => setEmail(event.currentTarget.value)}
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium">Password</span>
            <input
              className={input}
              type="password"
              autoComplete={registering ? 'new-password' : 'current-password'}
              required
              value={password}
              onInput={(event) => setPassword(event.currentTarget.value)}
            />
          </label>

          <button
            className="w-full rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
            type="submit"
            disabled={busy}
          >
            {registering ? 'Create account' : 'Sign in'}
          </button>

          <p className="text-sm">
            <a
              className="underline-offset-4 hover:underline"
              href={registering ? '/login' : '/register'}
            >
              {registering ? 'Already have an account?' : 'No account yet?'}
            </a>
          </p>
        </form>
      </div>
    </main>
  )
}
