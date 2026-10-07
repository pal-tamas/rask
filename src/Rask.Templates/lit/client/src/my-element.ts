import { LitElement, html } from 'lit'
import { customElement, state } from 'lit/decorators.js'
import { rask } from './rask/client'
import { login, register, type AuthFailure } from './rask/browser/auth'
import { getGreeting, recordVisit } from './rask/messages'
import type { Greeting } from './rask/contracts'

const input =
  'w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950'

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
      <main class="grid min-h-screen place-items-center px-4">
        <div class="flex w-full max-w-sm flex-col items-center gap-6">
          <h1 class="text-2xl font-bold">${registering ? 'Create an account' : 'Sign in'}</h1>

          <form
            class="flex w-full flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 shadow-xs dark:border-zinc-800 dark:bg-zinc-900"
            @submit=${this.submitAuth}
          >
            ${
              this.authFailure
                ? html`<div
                    role="alert"
                    class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
                  >
                    ${this.authFailure.message ?? this.authFailure.error}
                  </div>`
                : ''
            }

            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">Email</span>
              <input
                class=${input}
                type="email"
                autocomplete="username"
                required
                .value=${this.email}
                @input=${(event: Event) => {
                  this.email = (event.target as HTMLInputElement).value
                }}
              />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">Password</span>
              <input
                class=${input}
                type="password"
                autocomplete=${registering ? 'new-password' : 'current-password'}
                required
                .value=${this.password}
                @input=${(event: Event) => {
                  this.password = (event.target as HTMLInputElement).value
                }}
              />
            </label>

            <button
              class="w-full rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
              type="submit"
              ?disabled=${this.busy}
            >
              ${registering ? 'Create account' : 'Sign in'}
            </button>

            <p class="text-sm">
              <a
                class="underline-offset-4 hover:underline"
                href=${registering ? '/login' : '/register'}
              >
                ${registering ? 'Already have an account?' : 'No account yet?'}
              </a>
            </p>
          </form>
        </div>
      </main>
    `
  }

  render() {
    if (this.route === '/login' || this.route === '/register') return this.renderAuth()

    // Plain Tailwind utilities, spelled out in full: Tailwind emits a class only where it can
    // see the name, so a name built by concatenation styles nothing.
    return html`
      <div class="flex min-h-screen flex-col">
        <nav
          class="flex items-center justify-between border-b border-zinc-200 bg-white px-4 py-3 dark:border-zinc-800 dark:bg-zinc-900"
        >
          <span class="text-lg font-semibold tracking-tight">Rask + Lit</span>
          <a class="text-sm underline-offset-4 hover:underline" href="https://rask.sh/docs">Docs</a>
        </nav>

        <main class="grid grow place-items-center px-4 py-16">
          <div class="w-full max-w-md text-center">
            <h1 class="text-4xl font-bold">Rask + Lit</h1>
            <p class="py-4 text-zinc-500 dark:text-zinc-400">
              One query and one command, over your C# records.
            </p>

            <div
              class="flex flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 text-left shadow-xs dark:border-zinc-800 dark:bg-zinc-900"
            >
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">Name</span>
                <input
                  class=${input}
                  .value=${this.name}
                  @input=${(event: Event) => {
                    this.name = (event.target as HTMLInputElement).value
                    void this.load()
                  }}
                />
              </label>

              ${
                !this.greeting && !this.error
                  ? html`<span
                      class="size-4 animate-spin rounded-full border-2 border-zinc-300 border-t-zinc-900 dark:border-zinc-700 dark:border-t-zinc-100"
                      role="status"
                      aria-label="Loading"
                    ></span>`
                  : ''
              }
              ${this.error ? html`<div role="alert" class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200">${this.error}</div>` : ''}
              ${
                this.greeting
                  ? html`
                      <p>${this.greeting.message}</p>
                      <!-- seenAt is a real Date, revived because the C# type said so. -->
                      <p class="text-sm text-zinc-500 dark:text-zinc-400">
                        Server time:
                        ${new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(
                          this.greeting.seenAt,
                        )}
                      </p>
                      <div>
                        <div class="text-sm text-zinc-500 dark:text-zinc-400">Visits</div>
                        <div class="text-2xl font-semibold tabular-nums">
                          ${this.greeting.visits}
                        </div>
                      </div>
                    `
                  : ''
              }

              <div class="flex justify-end">
                <button
                  class="rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
                  ?disabled=${this.busy}
                  @click=${this.record}
                >
                  Record a visit
                </button>
              </div>
            </div>
          </div>
        </main>

        <footer
          class="border-t border-zinc-200 bg-white p-4 text-center text-sm text-zinc-500 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-400"
        >
          Built with Rask.
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
      this.greeting = await rask.dispatch(getGreeting({ name: this.name }), {
        signal: controller.signal,
      })
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
