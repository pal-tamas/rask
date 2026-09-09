namespace Rask.Cli.Scaffolding;

/// <summary>
///     The sign-in and registration screens each meta framework gets, in its own routing convention.
/// </summary>
/// <remarks>
///     <para>
///         The endpoints and a typed client both already ship: <c>Rask.Auth</c> maps <c>/api/auth</c>,
///         and the build writes <c>@rask/browser/auth</c> into every client. So these are a form over
///         two generated functions — <c>login</c> and <c>register</c> answer <c>{ok: true, user}</c> or
///         <c>{ok: false, failure}</c>, the request carries the CSRF header those endpoints require, and
///         the cookie they set is HttpOnly, so nothing here touches browser storage.
///     </para>
///     <para>
///         <b>Every path below was read off a real scaffold</b>, by running each creator, because a
///         route file at the wrong path does not fail — the framework simply never routes it, and the
///         page 404s on a green build. They are not uniform and not guessable: Analog and SolidStart
///         both default-export a component but from different directories, TanStack wraps its in
///         <c>createFileRoute</c>, and Next needs <c>'use client'</c> because App Router components are
///         server components by default and <c>useState</c> does not exist in one.
///     </para>
///     <para>
///         The markup is the same card the C# lane's <c>/login</c> draws, class for class, so a project
///         looks the same whichever front end scaffolded it.
///     </para>
/// </remarks>
internal static class MetaAuthPages
{
    /// <summary>Next.js — App Router. Interactive, so both routes are client components.</summary>
    public static IReadOnlyList<(string Path, string Content)> Next =>
    [
        ("app/auth-form.tsx", $$"""
            'use client'

            {{TsxComponent("@rask/browser/auth")}}
            """),

        ("app/login/page.tsx", """
            import AuthForm from '../auth-form'

            export default function LoginPage() {
              return <AuthForm mode="login" />
            }

            """),

        ("app/register/page.tsx", """
            import AuthForm from '../auth-form'

            export default function RegisterPage() {
              return <AuthForm mode="register" />
            }

            """),
    ];

    /// <summary>TanStack Start — file routes, each wrapped in <c>createFileRoute</c>.</summary>
    public static IReadOnlyList<(string Path, string Content)> TanStackStart =>
    [
        ("src/components/AuthForm.tsx", TsxComponent("@rask/browser/auth")),

        ("src/routes/login.tsx", """
            import { createFileRoute } from '@tanstack/react-router'

            import AuthForm from '../components/AuthForm'

            export const Route = createFileRoute('/login')({
              component: () => <AuthForm mode="login" />,
            })

            """),

        ("src/routes/register.tsx", """
            import { createFileRoute } from '@tanstack/react-router'

            import AuthForm from '../components/AuthForm'

            export const Route = createFileRoute('/register')({
              component: () => <AuthForm mode="register" />,
            })

            """),
    ];

    /// <summary>SolidStart — file routes that default-export a component.</summary>
    public static IReadOnlyList<(string Path, string Content)> SolidStart =>
    [
        ("src/components/AuthForm.tsx", SolidComponent),

        ("src/routes/login.tsx", """
            import AuthForm from '~/components/AuthForm'

            export default function Login() {
              return <AuthForm mode="login" />
            }

            """),

        ("src/routes/register.tsx", """
            import AuthForm from '~/components/AuthForm'

            export default function Register() {
              return <AuthForm mode="register" />
            }

            """),
    ];

    /// <summary>SvelteKit — a directory per route, each holding a <c>+page.svelte</c>.</summary>
    public static IReadOnlyList<(string Path, string Content)> SvelteKit =>
    [
        ("src/lib/AuthForm.svelte", SvelteComponent),

        ("src/routes/login/+page.svelte", """
            <script lang="ts">
              import AuthForm from '$lib/AuthForm.svelte'
            </script>

            <AuthForm mode="login" />

            """),

        ("src/routes/register/+page.svelte", """
            <script lang="ts">
              import AuthForm from '$lib/AuthForm.svelte'
            </script>

            <AuthForm mode="register" />

            """),
    ];

    /// <summary>Analog — <c>*.page.ts</c> files that default-export a standalone component.</summary>
    public static IReadOnlyList<(string Path, string Content)> Analog =>
    [
        ("src/app/pages/auth-form.ts", AngularComponent),

        ("src/app/pages/login.page.ts", """
            import { Component } from '@angular/core';

            import { AuthForm } from './auth-form';

            @Component({
              selector: 'app-login',
              imports: [AuthForm],
              template: `<app-auth-form mode="login" />`,
            })
            export default class Login {}

            """),

        ("src/app/pages/register.page.ts", """
            import { Component } from '@angular/core';

            import { AuthForm } from './auth-form';

            @Component({
              selector: 'app-register',
              imports: [AuthForm],
              template: `<app-auth-form mode="register" />`,
            })
            export default class Register {}

            """),
    ];

    /// <summary>
    ///     Nuxt — which needs its pages router introduced before it has routes at all.
    /// </summary>
    /// <remarks>
    ///     The minimal template writes no <c>pages/</c> directory: <c>app/app.vue</c> renders
    ///     <c>&lt;NuxtWelcome /&gt;</c> and that is the whole application. Creating <c>pages/</c> is what
    ///     turns vue-router on, and <c>app.vue</c> then has to render <c>&lt;NuxtPage /&gt;</c> or none of
    ///     them are reachable — which also means supplying an <c>index.vue</c>, or <c>/</c> starts
    ///     404ing. This is the one framework here where adding a route replaces something the creator
    ///     wrote, and it is the ordinary shape of every real Nuxt application.
    /// </remarks>
    public static IReadOnlyList<(string Path, string Content)> Nuxt =>
    [
        ("app/app.vue", """
            <template>
              <div>
                <NuxtRouteAnnouncer />
                <NuxtPage />
              </div>
            </template>

            """),

        ("app/pages/index.vue", """
            <template>
              <main class="hero min-h-screen bg-base-200">
                <div class="hero-content text-center">
                  <div class="max-w-md">
                    <h1 class="text-4xl font-bold">Rask + Nuxt</h1>
                    <p class="py-4 text-base-content/70">
                      Your app is running. Edit <code class="kbd kbd-sm">app/pages/index.vue</code>.
                    </p>
                    <a class="btn btn-primary" href="/login">Sign in</a>
                  </div>
                </div>
              </main>
            </template>

            """),

        ("app/components/AuthForm.vue", VueComponent),

        ("app/pages/login.vue", """
            <template>
              <AuthForm mode="login" />
            </template>

            """),

        ("app/pages/register.vue", """
            <template>
              <AuthForm mode="register" />
            </template>

            """),
    ];

    /// <summary>The React-flavoured form, shared by Next and TanStack Start.</summary>
    private static string TsxComponent(string client) => $$"""
        import { useState, type FormEvent } from 'react'

        import { login, register, type AuthFailure } from '{{client}}'

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

        """;

    private const string SolidComponent = """
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

        """;

    private const string SvelteComponent = """
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

        """;

    private const string VueComponent = """
        <script setup lang="ts">
        import { computed, ref } from 'vue'

        import { login, register, type AuthFailure } from '@rask/browser/auth'

        /*
          Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
          client is typed, so there is no status code to read and no shape to guess, and the cookie it
          sets is HttpOnly — this page never sees a token.
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

        """;

    private const string AngularComponent = """
        import { Component, computed, input, signal } from '@angular/core';

        import { login, register, type AuthFailure } from '@rask/browser/auth';

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

        """;
}
