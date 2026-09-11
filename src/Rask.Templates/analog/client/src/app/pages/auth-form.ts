import { Component, computed, input, signal } from '@angular/core'

import { login, register, type AuthFailure } from '@rask/browser/auth'

/**
 * Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
 * client is typed, so there is no status code to read and no shape to guess, and the cookie it
 * sets is HttpOnly — this page never sees a token.
 */
@Component({
  selector: 'app-auth-form',
  template: `
    <main class="hero min-h-screen bg-base-200">
      <div class="hero-content w-full max-w-sm flex-col">
        <h1 class="text-2xl font-bold">
          {{ registering() ? 'Create an account' : 'Sign in' }}
        </h1>

        <!-- (submit), not (ngSubmit): that one is FormsModule's, and this component does not
             import it — the binding would simply never fire. -->
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
export class AuthForm {
  readonly mode = input.required<'login' | 'register'>()

  protected readonly registering = computed(() => this.mode() === 'register')
  protected readonly email = signal('')
  protected readonly password = signal('')
  protected readonly failure = signal<AuthFailure | null>(null)
  protected readonly busy = signal(false)

  protected async submit(event: Event): Promise<void> {
    event.preventDefault()
    this.busy.set(true)
    this.failure.set(null)

    const credentials = { email: this.email(), password: this.password() }
    const result = this.registering() ? await register(credentials) : await login(credentials)

    this.busy.set(false)
    if (result.ok) window.location.assign('/')
    else this.failure.set(result.failure)
  }
}
