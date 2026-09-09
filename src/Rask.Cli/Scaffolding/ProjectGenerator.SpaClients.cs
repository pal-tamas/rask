namespace Rask.Cli.Scaffolding;

/// <summary>
///     The client files Rask overlays onto each framework's own scaffold.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately small. Everything else in a scaffolded client is <c>create-vite</c>'s, and the
///         overlay growing is the signal that the split has stopped working — a React skeleton Rask
///         maintained by hand would be a worse React skeleton within a release or two.
///     </para>
///     <para>
///         Each framework gets the same starter: one query, one command, a date that arrives as a
///         <c>Date</c>, and an invalidation keyed on the factory's own wire name rather than a string
///         literal. Written per framework rather than generated from one shape, because the adapters
///         genuinely differ — Solid and Svelte take a <em>thunk</em> so the options re-read their
///         reactive source, React does not, and Lit hands the client down through a custom element.
///     </para>
///     <para>
///         None of them imports the scaffolder's per-component demo stylesheet (create-vite's
///         <c>App.css</c> / <c>app.css</c>). That file styles the placeholder page — <c>.counter</c>,
///         <c>.hero</c>, <c>#next-steps</c> — and this overlay replaces that page entirely, so every one
///         of its selectors matches nothing. It was loaded and dead in every scaffolded project. The
///         GLOBAL stylesheet is a different file and stays: it styles <c>body</c> and the headings by
///         tag, which the overlay does still render. Angular is the exception and keeps its
///         <c>styleUrl</c> — <c>ng new</c> writes an empty <c>app.css</c>, which is the idiomatic place
///         to put component styles rather than dead demo CSS.
///     </para>
/// </remarks>
internal static class SpaClientSources
{
    public static IReadOnlyList<(string Path, string Content)> React =>
    [
        ("src/main.tsx", """
            import { StrictMode } from 'react'
            import { createRoot } from 'react-dom/client'
            import App from './App'
            import Auth from './Auth'
            import './index.css'

            // No router, deliberately. Which one to reach for is a decision a front-end developer has
            // usually already made, and scaffolding one would make it for them — so this reads the path
            // once, and the day you add a router it is three lines to delete. Deep links work already:
            // the dev server and the host both fall back to index.html for an unknown path.
            const path = window.location.pathname

            createRoot(document.getElementById('root')!).render(
              <StrictMode>
                {path === '/login' ? (
                  <Auth mode="login" />
                ) : path === '/register' ? (
                  <Auth mode="register" />
                ) : (
                  <App />
                )}
              </StrictMode>,
            )

            """),

        ("src/Auth.tsx", """
            import { useState, type FormEvent } from 'react'
            import { login, register, type AuthFailure } from './rask/browser/auth'

            /**
             * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth.
             *
             * The client is generated into rask/browser and typed: `login` answers
             * `{ok: true, user}` or `{ok: false, failure}`, so there is no status code to read and no
             * shape to guess. The cookie it sets is HttpOnly — this page never sees a token, so there
             * is nothing here to keep in browser storage.
             */
            export default function Auth({ mode }: { mode: 'login' | 'register' }) {
              const registering = mode === 'register'
              const [email, setEmail] = useState('')
              const [password, setPassword] = useState('')
              const [failure, setFailure] = useState<AuthFailure | null>(null)
              const [busy, setBusy] = useState(false)

              async function submit(event: FormEvent) {
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

            """),

        ("src/App.tsx", """
            import { useCallback, useEffect, useState } from 'react'
            import { rask } from './rask/client'
            import { getGreeting, recordVisit } from './rask/messages'
            import type { Greeting } from './rask/contracts'

            export default function App() {
              const [name, setName] = useState('world')
              const [greeting, setGreeting] = useState<Greeting | null>(null)
              const [error, setError] = useState<string | null>(null)
              const [busy, setBusy] = useState(false)

              // rask.dispatch and nothing else. The message carries its own result type, so `greeting` is
              // a Greeting with no cast and no wire name spelled out here — renaming a property in the C#
              // record breaks this at build time rather than on the wire.
              const load = useCallback(async (signal?: AbortSignal) => {
                setError(null)
                try {
                  setGreeting(await rask.dispatch(getGreeting({ name }), { signal }))
                } catch (e) {
                  // An aborted request is the previous keystroke being superseded, not a failure.
                  if (!signal?.aborted) setError(e instanceof Error ? e.message : String(e))
                }
              }, [name])

              // Refetch as the name changes, and abort the request in flight so a slow earlier one
              // cannot land after a later one and show the wrong answer.
              useEffect(() => {
                const controller = new AbortController()
                void load(controller.signal)
                return () => controller.abort()
              }, [load])

              const visit = async () => {
                setBusy(true)
                try {
                  await rask.dispatch(recordVisit({ name }))
                  await load()
                } finally {
                  setBusy(false)
                }
              }

              // daisyUI's own class names, and the same navbar / hero / card / footer skeleton every
              // other `rask new` template draws — so a project looks the same whichever front end it
              // was scaffolded with. Spelled out in full: Tailwind emits a class only where it can see
              // the name, so a name built by concatenation styles nothing.
              return (
                <div className="flex min-h-screen flex-col bg-base-200">
                  <nav className="navbar bg-base-100 shadow-sm">
                    <div className="navbar-start">
                      <span className="px-2 text-lg font-semibold tracking-tight">Rask + React</span>
                    </div>
                    <div className="navbar-end">
                      <a className="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
                    </div>
                  </nav>

                  <main className="hero grow py-16">
                    <div className="hero-content text-center">
                      <div className="max-w-md">
                        <h1 className="text-4xl font-bold">Rask + React</h1>
                        <p className="py-4 text-base-content/70">
                          One query and one command, over your C# records.
                        </p>

                        <div className="card bg-base-100 w-full max-w-md shadow-sm">
                          <div className="card-body gap-4 text-left">
                            <label className="fieldset">
                              <span className="fieldset-legend">Name</span>
                              <input
                                className="input w-full"
                                value={name}
                                onChange={(event) => setName(event.target.value)}
                              />
                            </label>

                            {!greeting && !error && (
                              <span className="loading loading-spinner loading-sm" aria-label="Loading" />
                            )}
                            {error && (
                              <div role="alert" className="alert alert-error"><span>{error}</span></div>
                            )}

                            {greeting && (
                              <>
                                <p>{greeting.message}</p>
                                {/* seenAt is a real Date, revived because the C# type said it was an
                                    instant — not because the string looked like one. Formatting is the
                                    browser's job: `undefined` means the visitor's own locale, and their
                                    own time zone. */}
                                <p className="text-sm text-base-content/70">
                                  Server time:{' '}
                                  {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
                                </p>
                                <div className="stat p-0">
                                  <div className="stat-title">Visits</div>
                                  <div className="stat-value text-2xl">{greeting.visits}</div>
                                </div>
                              </>
                            )}

                            <div className="card-actions justify-end">
                              <button className="btn btn-primary" onClick={visit} disabled={busy}>
                                Record a visit
                              </button>
                            </div>
                          </div>
                        </div>
                      </div>
                    </div>
                  </main>

                  <footer className="footer footer-center bg-base-100 p-4 text-base-content/70">
                    <aside><p>Built with Rask.</p></aside>
                  </footer>
                </div>
              )
            }

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Preact =>
    [
        ("src/main.tsx", """
            import { render } from 'preact'
            import { App } from './app'
            import { Auth } from './auth'
            import './index.css'

            // No router, deliberately — see the note in the React template. The path is read once, and
            // deep links work because the dev server and the host both fall back to index.html.
            const path = window.location.pathname
            const root = document.getElementById('app')!

            if (path === '/login') render(<Auth mode="login" />, root)
            else if (path === '/register') render(<Auth mode="register" />, root)
            else render(<App />, root)

            """),

        ("src/auth.tsx", """
            import { useState } from 'preact/hooks'
            import { login, register, type AuthFailure } from './rask/browser/auth'

            /**
             * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
             * client is typed, so there is no status code to read and no shape to guess, and the cookie
             * it sets is HttpOnly — this page never sees a token.
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
                            onInput={(event) => setEmail((event.target as HTMLInputElement).value)}
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
                            onInput={(event) => setPassword((event.target as HTMLInputElement).value)}
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

            """),

        ("src/app.tsx", """
            import { useCallback, useEffect, useState } from 'preact/hooks'
            import { rask } from './rask/client'
            import { getGreeting, recordVisit } from './rask/messages'
            import type { Greeting } from './rask/contracts'

            export function App() {
              const [name, setName] = useState('world')
              const [greeting, setGreeting] = useState<Greeting | null>(null)
              const [error, setError] = useState<string | null>(null)
              const [busy, setBusy] = useState(false)

              // The message carries its own result type, so `greeting` is a Greeting with no cast and no
              // wire name spelled out here.
              const load = useCallback(async (signal?: AbortSignal) => {
                setError(null)
                try {
                  setGreeting(await rask.dispatch(getGreeting({ name }), { signal }))
                } catch (e) {
                  if (!signal?.aborted) setError(e instanceof Error ? e.message : String(e))
                }
              }, [name])

              useEffect(() => {
                const controller = new AbortController()
                void load(controller.signal)
                return () => controller.abort()
              }, [load])

              const visit = async () => {
                setBusy(true)
                try {
                  await rask.dispatch(recordVisit({ name }))
                  await load()
                } finally {
                  setBusy(false)
                }
              }

              return (
                <div className="flex min-h-screen flex-col bg-base-200">
                  <nav className="navbar bg-base-100 shadow-sm">
                    <div className="navbar-start">
                      <span className="px-2 text-lg font-semibold tracking-tight">Rask + Preact</span>
                    </div>
                    <div className="navbar-end">
                      <a className="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
                    </div>
                  </nav>

                  <main className="hero grow py-16">
                    <div className="hero-content text-center">
                      <div className="max-w-md">
                        <h1 className="text-4xl font-bold">Rask + Preact</h1>
                        <p className="py-4 text-base-content/70">
                          One query and one command, over your C# records.
                        </p>

                        <div className="card bg-base-100 w-full max-w-md shadow-sm">
                          <div className="card-body gap-4 text-left">
                            <label className="fieldset">
                              <span className="fieldset-legend">Name</span>
                              <input
                                className="input w-full"
                                value={name}
                                onInput={(event) => setName((event.target as HTMLInputElement).value)}
                              />
                            </label>

                            {!greeting && !error && (
                              <span className="loading loading-spinner loading-sm" aria-label="Loading" />
                            )}
                            {error && (
                              <div role="alert" className="alert alert-error"><span>{error}</span></div>
                            )}

                            {greeting && (
                              <>
                                <p>{greeting.message}</p>
                                {/* seenAt is a real Date, revived because the C# type said it was an instant. */}
                                <p className="text-sm text-base-content/70">
                                  Server time:{' '}
                                  {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
                                </p>
                                <div className="stat p-0">
                                  <div className="stat-title">Visits</div>
                                  <div className="stat-value text-2xl">{greeting.visits}</div>
                                </div>
                              </>
                            )}

                            <div className="card-actions justify-end">
                              <button className="btn btn-primary" onClick={visit} disabled={busy}>
                                Record a visit
                              </button>
                            </div>
                          </div>
                        </div>
                      </div>
                    </div>
                  </main>

                  <footer className="footer footer-center bg-base-100 p-4 text-base-content/70">
                    <aside><p>Built with Rask.</p></aside>
                  </footer>
                </div>
              )
            }

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Solid =>
    [
        ("src/index.tsx", """
            /* @refresh reload */
            import { render } from 'solid-js/web'
            import App from './App'
            import Auth from './Auth'
            import './index.css'

            // No router, deliberately — see the note in the React template. The path is read once, and
            // deep links work because the dev server and the host both fall back to index.html.
            const path = window.location.pathname
            const root = document.getElementById('root')!

            if (path === '/login') render(() => <Auth mode="login" />, root)
            else if (path === '/register') render(() => <Auth mode="register" />, root)
            else render(() => <App />, root)

            """),

        ("src/Auth.tsx", """
            import { Show, createSignal } from 'solid-js'
            import { login, register, type AuthFailure } from './rask/browser/auth'

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
                const result = registering()
                  ? await register(credentials)
                  : await login(credentials)

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
                          {(error) => (
                            <div role="alert" class="alert alert-error">
                              <span>{error().message ?? error().error}</span>
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

            """),

        ("src/App.tsx", """
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

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Vue =>
    [
        ("src/main.ts", """
            import { createApp, h } from 'vue'
            import './style.css'
            import App from './App.vue'
            import Auth from './Auth.vue'

            // No router, deliberately — see the note in the React template. The path is read once, and
            // deep links work because the dev server and the host both fall back to index.html.
            const path = window.location.pathname
            const root =
              path === '/login' || path === '/register'
                ? h(Auth, { mode: path === '/register' ? 'register' : 'login' })
                : h(App)

            createApp(root).mount('#app')

            """),

        ("src/Auth.vue", """
            <script setup lang="ts">
            import { computed, ref } from 'vue'
            import { login, register, type AuthFailure } from './rask/browser/auth'

            /*
              Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
              client is typed, so there is no status code to read and no shape to guess, and the cookie
              it sets is HttpOnly — this page never sees a token.
            */
            const props = defineProps<{ mode: 'login' | 'register' }>()

            const registering = computed(() => props.mode === 'register')
            const email = ref('')
            const password = ref('')
            const failure = ref<AuthFailure | null>(null)
            const busy = ref(false)

            async function submit() {
              busy.value = true
              failure.value = null

              const credentials = { email: email.value, password: password.value }
              const result = registering.value
                ? await register(credentials)
                : await login(credentials)

              busy.value = false
              if (result.ok) window.location.assign('/')
              else failure.value = result.failure
            }
            </script>

            <template>
              <main class="hero min-h-screen bg-base-200">
                <div class="hero-content w-full max-w-sm flex-col">
                  <h1 class="text-2xl font-bold">
                    {{ registering ? 'Create an account' : 'Sign in' }}
                  </h1>

                  <form class="card bg-base-100 w-full shadow-sm" @submit.prevent="submit">
                    <div class="card-body gap-4">
                      <div v-if="failure" role="alert" class="alert alert-error">
                        <span>{{ failure.message ?? failure.error }}</span>
                      </div>

                      <label class="fieldset">
                        <span class="fieldset-legend">Email</span>
                        <input
                          class="input w-full"
                          type="email"
                          autocomplete="username"
                          required
                          v-model="email"
                        />
                      </label>

                      <label class="fieldset">
                        <span class="fieldset-legend">Password</span>
                        <input
                          class="input w-full"
                          type="password"
                          :autocomplete="registering ? 'new-password' : 'current-password'"
                          required
                          v-model="password"
                        />
                      </label>

                      <div class="card-actions">
                        <button class="btn btn-primary btn-block" type="submit" :disabled="busy">
                          {{ registering ? 'Create account' : 'Sign in' }}
                        </button>
                      </div>

                      <p class="text-sm">
                        <a class="link link-primary" :href="registering ? '/login' : '/register'">
                          {{ registering ? 'Already have an account?' : 'No account yet?' }}
                        </a>
                      </p>
                    </div>
                  </form>
                </div>
              </main>
            </template>

            """),

        ("src/App.vue", """
            <script setup lang="ts">
            import { computed, ref, watch } from 'vue'
            import { rask } from './rask/client'
            import { getGreeting, recordVisit } from './rask/messages'
            import type { Greeting } from './rask/contracts'

            const name = ref('world')
            const greeting = ref<Greeting | null>(null)
            const error = ref<string | null>(null)
            const busy = ref(false)

            // watch with immediate, not a one-off call: that is what re-reads the ref and refetches when
            // it changes. The AbortController is what stops a slow earlier request landing after a later
            // one and showing the wrong answer.
            let inFlight: AbortController | null = null

            async function load() {
              inFlight?.abort()
              const controller = new AbortController()
              inFlight = controller
              error.value = null

              try {
                greeting.value = await rask.dispatch(
                  getGreeting({ name: name.value }),
                  { signal: controller.signal },
                )
              } catch (e) {
                if (!controller.signal.aborted) error.value = e instanceof Error ? e.message : String(e)
              }
            }

            watch(name, load, { immediate: true })

            async function visit() {
              busy.value = true
              try {
                await rask.dispatch(recordVisit({ name: name.value }))
                await load()
              } finally {
                busy.value = false
              }
            }

            const serverTime = computed(() =>
              greeting.value
                ? new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.value.seenAt)
                : '',
            )
            </script>

            <template>
              <div class="flex min-h-screen flex-col bg-base-200">
                <nav class="navbar bg-base-100 shadow-sm">
                  <div class="navbar-start">
                    <span class="px-2 text-lg font-semibold tracking-tight">Rask + Vue</span>
                  </div>
                  <div class="navbar-end">
                    <a class="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
                  </div>
                </nav>

                <main class="hero grow py-16">
                  <div class="hero-content text-center">
                    <div class="max-w-md">
                      <h1 class="text-4xl font-bold">Rask + Vue</h1>
                      <p class="py-4 text-base-content/70">
                        One query and one command, over your C# records.
                      </p>

                      <div class="card bg-base-100 w-full max-w-md shadow-sm">
                        <div class="card-body gap-4 text-left">
                          <label class="fieldset">
                            <span class="fieldset-legend">Name</span>
                            <input class="input w-full" v-model="name" />
                          </label>

                          <span
                            v-if="!greeting && !error"
                            class="loading loading-spinner loading-sm"
                            aria-label="Loading"
                          />
                          <div v-else-if="error" role="alert" class="alert alert-error">
                            <span>{{ error }}</span>
                          </div>

                          <template v-if="greeting">
                            <p>{{ greeting.message }}</p>
                            <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
                            <p class="text-sm text-base-content/70">Server time: {{ serverTime }}</p>
                            <div class="stat p-0">
                              <div class="stat-title">Visits</div>
                              <div class="stat-value text-2xl">{{ greeting.visits }}</div>
                            </div>
                          </template>

                          <div class="card-actions justify-end">
                            <button class="btn btn-primary" :disabled="busy" @click="visit">
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
            </template>

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Svelte =>
    [
        ("src/App.svelte", """
            <script lang="ts">
              import Greeting from './lib/Greeting.svelte'
              import Auth from './lib/Auth.svelte'

              // No router, deliberately — see the note in the React template. The path is read once,
              // and deep links work because the dev server and the host both fall back to index.html.
              const path = window.location.pathname
            </script>

            {#if path === '/login'}
              <Auth mode="login" />
            {:else if path === '/register'}
              <Auth mode="register" />
            {:else}
              <Greeting />
            {/if}

            """),

        ("src/lib/Auth.svelte", """
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

            """),

        ("src/lib/Greeting.svelte", """
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

            """),
    ];

    /// <summary>
    ///     Angular, which is the one framework here that <c>create-vite</c> does not scaffold.
    /// </summary>
    /// <remarks>
    ///     Its own CLI writes the client, and three things differ as a result: the bundle lands in
    ///     <c>dist/&lt;project&gt;/browser</c>, the dev server is <c>ng serve</c> on port 4200, and the
    ///     proxy is declared in <c>proxy.conf.json</c> and pointed at from <c>angular.json</c> rather than
    ///     in a Vite config — there is no <c>vite.config.ts</c> to write. Angular's build has used Vite
    ///     under the hood since v17, but you do not configure it.
    /// </remarks>
    public static IReadOnlyList<(string Path, string Content)> Angular =>
    [
        ("proxy.conf.json", """
            {
              "//": "In development the browser talks to `ng serve`, which forwards the CQRS calls to the ASP.NET host — so the browser only ever sees one origin and there is no CORS to configure. angular.json points at this file. In production it is not used at all: the host serves the built bundle and answers /_rask itself.",
              "/_rask": {
                "target": "http://localhost:5000",
                "secure": false
              },
              "/api/auth": {
                "target": "http://localhost:5000",
                "secure": false
              }
            }

            """),

        ("src/app/app.config.ts", """
            import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
            import { provideRouter } from '@angular/router';

            import { routes } from './app.routes';

            export const appConfig: ApplicationConfig = {
              providers: [
                provideBrowserGlobalErrorListeners(),
                provideRouter(routes),
              ],
            };

            """),

        ("src/app/app.ts", """
            import { Component, effect, signal } from '@angular/core';

            import { Auth } from './auth';
            import { rask } from '../rask/client';
            import { getGreeting, recordVisit } from '../rask/messages';
            import type { Greeting } from '../rask/contracts';

            @Component({
              selector: 'app-root',
              templateUrl: './app.html',
              styleUrl: './app.css',
              imports: [Auth],
            })
            export class App {
              // No router, deliberately — see the note in the React template. Angular's is provided in
              // app.config.ts and app.routes.ts is yours to fill in; this reads the path once so the
              // starter does not decide your routing for you.
              protected readonly route = window.location.pathname;

              protected readonly name = signal('world');
              protected readonly greeting = signal<Greeting | null>(null);
              protected readonly error = signal<string | null>(null);
              protected readonly busy = signal(false);

              private inFlight: AbortController | null = null;

              constructor() {
                // Reading name() inside an effect is what makes this refetch when the name changes.
                // Aborting the previous request is what stops a slow earlier one landing after a later
                // one and showing the wrong answer.
                effect(() => {
                  const value = this.name();
                  void this.load(value);
                });
              }

              protected setName(value: string): void {
                this.name.set(value);
              }

              protected async record(): Promise<void> {
                this.busy.set(true);
                try {
                  await rask.dispatch(recordVisit({ name: this.name() }));
                  await this.load(this.name());
                } finally {
                  this.busy.set(false);
                }
              }

              // seenAt is a real Date, revived because the C# type said it was an instant. `undefined` as
              // the locale means the visitor's own, and their own time zone.
              protected time(value: Date): string {
                return new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(value);
              }

              private async load(name: string): Promise<void> {
                this.inFlight?.abort();
                const controller = new AbortController();
                this.inFlight = controller;
                this.error.set(null);

                try {
                  // The message carries its own result type, so this is a Greeting with no cast and no
                  // wire name spelled out here.
                  this.greeting.set(await rask.dispatch(getGreeting({ name }), { signal: controller.signal }));
                } catch (e) {
                  if (!controller.signal.aborted) {
                    this.error.set(e instanceof Error ? e.message : String(e));
                  }
                }
              }
            }

            """),

        ("src/app/app.html", """
            @if (route === '/login' || route === '/register') {
              <app-auth [mode]="route === '/register' ? 'register' : 'login'" />
            } @else {
            <div class="flex min-h-screen flex-col bg-base-200">
              <nav class="navbar bg-base-100 shadow-sm">
                <div class="navbar-start">
                  <span class="px-2 text-lg font-semibold tracking-tight">Rask + Angular</span>
                </div>
                <div class="navbar-end">
                  <a class="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
                </div>
              </nav>

              <main class="hero grow py-16">
                <div class="hero-content text-center">
                  <div class="max-w-md">
                    <h1 class="text-4xl font-bold">Rask + Angular</h1>
                    <p class="py-4 text-base-content/70">
                      One query and one command, over your C# records.
                    </p>

                    <div class="card bg-base-100 w-full max-w-md shadow-sm">
                      <div class="card-body gap-4 text-left">
                        <label class="fieldset">
                          <span class="fieldset-legend">Name</span>
                          <input
                            class="input w-full"
                            [value]="name()"
                            (input)="setName($any($event.target).value)"
                          />
                        </label>

                        @if (!greeting() && !error()) {
                          <span class="loading loading-spinner loading-sm" aria-label="Loading"></span>
                        }

                        @if (error(); as message) {
                          <div role="alert" class="alert alert-error"><span>{{ message }}</span></div>
                        }

                        @if (greeting(); as data) {
                          <p>{{ data.message }}</p>
                          <p class="text-sm text-base-content/70">Server time: {{ time(data.seenAt) }}</p>
                          <div class="stat p-0">
                            <div class="stat-title">Visits</div>
                            <div class="stat-value text-2xl">{{ data.visits }}</div>
                          </div>
                        }

                        <div class="card-actions justify-end">
                          <button class="btn btn-primary" [disabled]="busy()" (click)="record()">
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
            }

            """),

        ("src/app/auth.ts", """
            import { Component, computed, input, signal } from '@angular/core';

            import { login, register, type AuthFailure } from '../rask/browser/auth';

            /**
             * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
             * client is typed, so there is no status code to read and no shape to guess, and the cookie
             * it sets is HttpOnly — this page never sees a token.
             *
             * An inline template rather than a templateUrl, to keep the overlay Rask maintains by hand
             * as small as it can be: everything else under src/ is Angular's own scaffold.
             */
            @Component({
              selector: 'app-auth',
              template: `
                <main class="hero min-h-screen bg-base-200">
                  <div class="hero-content w-full max-w-sm flex-col">
                    <h1 class="text-2xl font-bold">
                      {{ registering() ? 'Create an account' : 'Sign in' }}
                    </h1>

                    <!-- (submit), not (ngSubmit): that one is FormsModule's, and this component does
                         not import it — the binding would simply never fire. -->
                    <form class="card bg-base-100 w-full shadow-sm" (submit)="submit($event)">
                      <div class="card-body gap-4">
                        @if (failure(); as problem) {
                          <div role="alert" class="alert alert-error">
                            <span>{{ problem.message ?? problem.error }}</span>
                          </div>
                        }

                        <label class="fieldset">
                          <span class="fieldset-legend">Email</span>
                          <input
                            class="input w-full"
                            type="email"
                            autocomplete="username"
                            required
                            [value]="email()"
                            (input)="email.set($any($event.target).value)"
                          />
                        </label>

                        <label class="fieldset">
                          <span class="fieldset-legend">Password</span>
                          <input
                            class="input w-full"
                            type="password"
                            [attr.autocomplete]="registering() ? 'new-password' : 'current-password'"
                            required
                            [value]="password()"
                            (input)="password.set($any($event.target).value)"
                          />
                        </label>

                        <div class="card-actions">
                          <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">
                            {{ registering() ? 'Create account' : 'Sign in' }}
                          </button>
                        </div>

                        <p class="text-sm">
                          <a class="link link-primary" [href]="registering() ? '/login' : '/register'">
                            {{ registering() ? 'Already have an account?' : 'No account yet?' }}
                          </a>
                        </p>
                      </div>
                    </form>
                  </div>
                </main>
              `,
            })
            export class Auth {
              readonly mode = input.required<'login' | 'register'>();

              protected readonly registering = computed(() => this.mode() === 'register');
              protected readonly email = signal('');
              protected readonly password = signal('');
              protected readonly failure = signal<AuthFailure | null>(null);
              protected readonly busy = signal(false);

              protected async submit(event: Event): Promise<void> {
                event.preventDefault();
                this.busy.set(true);
                this.failure.set(null);

                const credentials = { email: this.email(), password: this.password() };
                const result = this.registering()
                  ? await register(credentials)
                  : await login(credentials);

                this.busy.set(false);
                if (result.ok) window.location.assign('/');
                else this.failure.set(result.failure);
              }
            }

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Lit =>
    [
        ("src/my-element.ts", """
            import { LitElement, html } from 'lit'
            import { customElement, state } from 'lit/decorators.js'
            import { rask } from './rask/client'
            import { login, register, type AuthFailure } from './rask/browser/auth'
            import { getGreeting, recordVisit } from './rask/messages'
            import type { Greeting } from './rask/contracts'

            @customElement('my-element')
            export class MyElement extends LitElement {
              // Light DOM, on purpose. Lit renders into a shadow root by default, and page-level CSS does
              // not cross one — so the app's stylesheet (Tailwind's included) would style everything on
              // the page EXCEPT this component, with nothing reporting it. That trade buys encapsulation
              // for a component that declares no `static styles` of its own, so it costs more than it
              // gives here. Delete this to get the shadow root back, and move styling into `static
              // styles` when you do.
              protected createRenderRoot() {
                return this
              }

              @state()
              private accessor name = 'world'

              @state()
              private accessor greeting: Greeting | null = null

              @state()
              private accessor error: string | null = null

              @state()
              private accessor busy = false

              @state()
              private accessor authFailure: AuthFailure | null = null

              @state()
              private accessor email = ''

              @state()
              private accessor password = ''

              // No router, deliberately — see the note in the React template. Read once, so the day you
              // add one it is this line and the branch in render() to delete. Both screens live in this
              // element rather than a second one, because the light-DOM override above is what lets the
              // page's stylesheet reach them and it would have to be repeated verbatim there.
              private readonly route = window.location.pathname

              private inFlight: AbortController | null = null

              connectedCallback() {
                super.connectedCallback()
                if (this.route !== '/login' && this.route !== '/register') void this.load()
              }

              disconnectedCallback() {
                super.disconnectedCallback()
                this.inFlight?.abort()
              }

              private async submitAuth(event: Event) {
                event.preventDefault()
                this.busy = true
                this.authFailure = null

                const credentials = { email: this.email, password: this.password }
                const result =
                  this.route === '/register' ? await register(credentials) : await login(credentials)

                this.busy = false
                if (result.ok) window.location.assign('/')
                else this.authFailure = result.failure
              }

              /**
               * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
               * client is typed, so there is no status code to read and no shape to guess, and the
               * cookie it sets is HttpOnly — this page never sees a token.
               */
              private renderAuth() {
                const registering = this.route === '/register'

                return html`
                  <main class="hero min-h-screen bg-base-200">
                    <div class="hero-content w-full max-w-sm flex-col">
                      <h1 class="text-2xl font-bold">
                        ${registering ? 'Create an account' : 'Sign in'}
                      </h1>

                      <form class="card bg-base-100 w-full shadow-sm" @submit=${this.submitAuth}>
                        <div class="card-body gap-4">
                          ${this.authFailure
                            ? html`<div role="alert" class="alert alert-error">
                                <span>${this.authFailure.message ?? this.authFailure.error}</span>
                              </div>`
                            : ''}

                          <label class="fieldset">
                            <span class="fieldset-legend">Email</span>
                            <input
                              class="input w-full"
                              type="email"
                              autocomplete="username"
                              required
                              .value=${this.email}
                              @input=${(event: Event) => {
                                this.email = (event.target as HTMLInputElement).value
                              }}
                            />
                          </label>

                          <label class="fieldset">
                            <span class="fieldset-legend">Password</span>
                            <input
                              class="input w-full"
                              type="password"
                              autocomplete=${registering ? 'new-password' : 'current-password'}
                              required
                              .value=${this.password}
                              @input=${(event: Event) => {
                                this.password = (event.target as HTMLInputElement).value
                              }}
                            />
                          </label>

                          <div class="card-actions">
                            <button class="btn btn-primary btn-block" type="submit" ?disabled=${this.busy}>
                              ${registering ? 'Create account' : 'Sign in'}
                            </button>
                          </div>

                          <p class="text-sm">
                            <a class="link link-primary" href=${registering ? '/login' : '/register'}>
                              ${registering ? 'Already have an account?' : 'No account yet?'}
                            </a>
                          </p>
                        </div>
                      </form>
                    </div>
                  </main>
                `
              }

              render() {
                if (this.route === '/login' || this.route === '/register') return this.renderAuth()

                return html`
                  <div class="flex min-h-screen flex-col bg-base-200">
                    <nav class="navbar bg-base-100 shadow-sm">
                      <div class="navbar-start">
                        <span class="px-2 text-lg font-semibold tracking-tight">Rask + Lit</span>
                      </div>
                      <div class="navbar-end">
                        <a class="link link-hover link-primary" href="https://rask.sh/docs">Docs</a>
                      </div>
                    </nav>

                    <main class="hero grow py-16">
                      <div class="hero-content text-center">
                        <div class="max-w-md">
                          <h1 class="text-4xl font-bold">Rask + Lit</h1>
                          <p class="py-4 text-base-content/70">
                            One query and one command, over your C# records.
                          </p>

                          <div class="card bg-base-100 w-full max-w-md shadow-sm">
                            <div class="card-body gap-4 text-left">
                              <label class="fieldset">
                                <span class="fieldset-legend">Name</span>
                                <input
                                  class="input w-full"
                                  .value=${this.name}
                                  @input=${(event: Event) => {
                                    this.name = (event.target as HTMLInputElement).value
                                    void this.load()
                                  }}
                                />
                              </label>

                              ${!this.greeting && !this.error
                                ? html`<span class="loading loading-spinner loading-sm" aria-label="Loading"></span>`
                                : ''}
                              ${this.error
                                ? html`<div role="alert" class="alert alert-error"><span>${this.error}</span></div>`
                                : ''}
                              ${this.greeting
                                ? html`
                                    <p>${this.greeting.message}</p>
                                    <!-- seenAt is a real Date, revived because the C# type said so. -->
                                    <p class="text-sm text-base-content/70">
                                      Server time:
                                      ${new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(
                                        this.greeting.seenAt,
                                      )}
                                    </p>
                                    <div class="stat p-0">
                                      <div class="stat-title">Visits</div>
                                      <div class="stat-value text-2xl">${this.greeting.visits}</div>
                                    </div>
                                  `
                                : ''}

                              <div class="card-actions justify-end">
                                <button class="btn btn-primary" ?disabled=${this.busy} @click=${this.record}>
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
                `
              }

              private record = async () => {
                this.busy = true
                try {
                  await rask.dispatch(recordVisit({ name: this.name }))
                  await this.load()
                } finally {
                  this.busy = false
                }
              }

              // Aborting the previous request is what stops a slow earlier one landing after a later one
              // and showing the wrong answer.
              private async load() {
                this.inFlight?.abort()
                const controller = new AbortController()
                this.inFlight = controller
                this.error = null

                try {
                  this.greeting = await rask.dispatch(
                    getGreeting({ name: this.name }),
                    { signal: controller.signal },
                  )
                } catch (e) {
                  if (!controller.signal.aborted) {
                    this.error = e instanceof Error ? e.message : String(e)
                  }
                }
              }
            }

            declare global {
              interface HTMLElementTagNameMap {
                'my-element': MyElement
              }
            }

            """),
    ];
}
