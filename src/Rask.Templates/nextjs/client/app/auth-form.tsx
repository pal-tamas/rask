'use client'

import { useState, type FormEvent } from 'react'

import { login, register, type AuthFailure } from '@rask/browser/auth'

/**
 * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
 * client is typed, so there is no status code to read and no shape to guess, and the cookie it
 * sets is HttpOnly — this page never sees a token.
 */
export default function AuthForm({ mode }: { mode: 'login' | 'register' }) {
  const registering = mode === 'register'
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [failure, setFailure] = useState<AuthFailure | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setFailure(null)

    const credentials = { email, password }
    const result = registering ? await register(credentials) : await login(credentials)

    setBusy(false)
    if (result.ok) window.location.assign('/')
    else setFailure(result.failure)
  }

  return (
    <main className="hero min-h-screen bg-base-200">
      <div className="hero-content w-full max-w-sm flex-col">
        <h1 className="text-2xl font-bold">
          {registering ? 'Create an account' : 'Sign in'}
        </h1>

        <form className="card bg-base-100 w-full shadow-sm" onSubmit={submit}>
          <div className="card-body gap-4">
            {failure && (
              <div role="alert" className="alert alert-error">
                <span>{failure.message ?? failure.error}</span>
              </div>
            )}

            <label className="fieldset">
              <span className="fieldset-legend">Email</span>
              <input
                className="input w-full"
                type="email"
                autoComplete="username"
                required
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            </label>

            <label className="fieldset">
              <span className="fieldset-legend">Password</span>
              <input
                className="input w-full"
                type="password"
                autoComplete={registering ? 'new-password' : 'current-password'}
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </label>

            <div className="card-actions">
              <button className="btn btn-primary btn-block" type="submit" disabled={busy}>
                {registering ? 'Create account' : 'Sign in'}
              </button>
            </div>

            <p className="text-sm">
              <a className="link link-primary" href={registering ? '/login' : '/register'}>
                {registering ? 'Already have an account?' : 'No account yet?'}
              </a>
            </p>
          </div>
        </form>
      </div>
    </main>
  )
}
