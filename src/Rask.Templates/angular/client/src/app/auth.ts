import { Component, computed, input, signal } from '@angular/core'

import { login, register, type AuthFailure } from '../rask/browser/auth'

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
    <main class="grid min-h-screen place-items-center px-4">
      <div class="flex w-full max-w-sm flex-col items-center gap-6">
        <h1 class="text-2xl font-bold">
          {{ registering() ? 'Create an account' : 'Sign in' }}
        </h1>

        <!-- (submit), not (ngSubmit): that one is FormsModule's, and this component does
             not import it — the binding would simply never fire. -->
        <form
          class="flex w-full flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 shadow-xs dark:border-zinc-800 dark:bg-zinc-900"
          (submit)="submit($event)"
        >
          @if (failure(); as problem) {
            <div
              role="alert"
              class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
            >
              {{ problem.message ?? problem.error }}
            </div>
          }

          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">Email</span>
            <input
              class="w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950"
              type="email"
              autocomplete="username"
              required
              [value]="email()"
              (input)="email.set($any($event.target).value)"
            />
          </label>

          <label class="flex flex-col gap-1">
            <span class="text-sm font-medium">Password</span>
            <input
              class="w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950"
              type="password"
              [attr.autocomplete]="registering() ? 'new-password' : 'current-password'"
              required
              [value]="password()"
              (input)="password.set($any($event.target).value)"
            />
          </label>

          <button
            class="w-full rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
            type="submit"
            [disabled]="busy()"
          >
            {{ registering() ? 'Create account' : 'Sign in' }}
          </button>

          <p class="text-sm">
            <a
              class="underline-offset-4 hover:underline"
              [href]="registering() ? '/login' : '/register'"
            >
              {{ registering() ? 'Already have an account?' : 'No account yet?' }}
            </a>
          </p>
        </form>
      </div>
    </main>
  `,
})
export class Auth {
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
