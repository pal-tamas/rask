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
          <h1 class="text-2xl font-bold">${registering ? 'Create an account' : 'Sign in'}</h1>

          <form class="card bg-base-100 w-full shadow-sm" @submit=${this.submitAuth}>
            <div class="card-body gap-4">
              ${
                this.authFailure
                  ? html`<div role="alert" class="alert alert-error">
                      <span>${this.authFailure.message ?? this.authFailure.error}</span>
                    </div>`
                  : ''
              }

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

                  ${
                    !this.greeting && !this.error
                      ? html`<span
                          class="loading loading-spinner loading-sm"
                          aria-label="Loading"
                        ></span>`
                      : ''
                  }
                  ${
                    this.error
                      ? html`<div role="alert" class="alert alert-error">
                          <span>${this.error}</span>
                        </div>`
                      : ''
                  }
                  ${
                    this.greeting
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
                      : ''
                  }

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
