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
            import './index.css'

            createRoot(document.getElementById('root')!).render(
              <StrictMode>
                <App />
              </StrictMode>,
            )

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

              return (
                <main>
                  <h1>Rask + React</h1>

                  <label>
                    Name <input value={name} onChange={(event) => setName(event.target.value)} />
                  </label>

                  {!greeting && !error && <p>Loading…</p>}
                  {error && <p role="alert">{error}</p>}

                  {greeting && (
                    <>
                      <p>{greeting.message}</p>
                      {/* seenAt is a real Date, revived because the C# type said it was an instant — not
                          because the string looked like one. Formatting is the browser's job: `undefined`
                          means the visitor's own locale, and their own time zone. */}
                      <p>
                        Server time:{' '}
                        {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
                      </p>
                      <p>Visits: {greeting.visits}</p>
                    </>
                  )}

                  <button onClick={visit} disabled={busy}>
                    Record a visit
                  </button>
                </main>
              )
            }

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Preact =>
    [
        ("src/main.tsx", """
            import { render } from 'preact'
            import { App } from './app'
            import './index.css'

            render(<App />, document.getElementById('app')!)

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
                <main>
                  <h1>Rask + Preact</h1>

                  <label>
                    Name{' '}
                    <input
                      value={name}
                      onInput={(event) => setName((event.target as HTMLInputElement).value)}
                    />
                  </label>

                  {!greeting && !error && <p>Loading…</p>}
                  {error && <p role="alert">{error}</p>}

                  {greeting && (
                    <>
                      <p>{greeting.message}</p>
                      {/* seenAt is a real Date, revived because the C# type said it was an instant. */}
                      <p>
                        Server time:{' '}
                        {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
                      </p>
                      <p>Visits: {greeting.visits}</p>
                    </>
                  )}

                  <button onClick={visit} disabled={busy}>
                    Record a visit
                  </button>
                </main>
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
            import './index.css'

            render(() => <App />, document.getElementById('root')!)

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
                <main>
                  <h1>Rask + Solid</h1>

                  <label>
                    Name{' '}
                    <input
                      value={name()}
                      onInput={(event) => setName(event.currentTarget.value)}
                    />
                  </label>

                  <Show when={greeting.loading}>
                    <p>Loading…</p>
                  </Show>
                  <Show when={greeting.error}>
                    {(error) => <p role="alert">{String(error())}</p>}
                  </Show>

                  <Show when={greeting()}>
                    {(data) => (
                      <>
                        <p>{data().message}</p>
                        {/* seenAt is a real Date, revived because the C# type said it was an instant. */}
                        <p>
                          Server time:{' '}
                          {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(data().seenAt)}
                        </p>
                        <p>Visits: {data().visits}</p>
                      </>
                    )}
                  </Show>

                  <button onClick={visit} disabled={busy()}>
                    Record a visit
                  </button>
                </main>
              )
            }

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Vue =>
    [
        ("src/main.ts", """
            import { createApp } from 'vue'
            import './style.css'
            import App from './App.vue'

            createApp(App).mount('#app')

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
              <main>
                <h1>Rask + Vue</h1>

                <label>
                  Name
                  <input v-model="name" />
                </label>

                <p v-if="!greeting && !error">Loading…</p>
                <p v-else-if="error" role="alert">{{ error }}</p>

                <template v-if="greeting">
                  <p>{{ greeting.message }}</p>
                  <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
                  <p>Server time: {{ serverTime }}</p>
                  <p>Visits: {{ greeting.visits }}</p>
                </template>

                <button :disabled="busy" @click="visit">
                  Record a visit
                </button>
              </main>
            </template>

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Svelte =>
    [
        ("src/App.svelte", """
            <script lang="ts">
              import Greeting from './lib/Greeting.svelte'
            </script>

            <Greeting />

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

            <main>
              <h1>Rask + Svelte</h1>

              <label>
                Name <input bind:value={name} />
              </label>

              {#if !greeting && !error}
                <p>Loading…</p>
              {:else if error}
                <p role="alert">{error}</p>
              {:else if greeting}
                <p>{greeting.message}</p>
                <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
                <p>
                  Server time:
                  {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
                </p>
                <p>Visits: {greeting.visits}</p>
              {/if}

              <button onclick={visit} disabled={busy}>
                Record a visit
              </button>
            </main>

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

            import { rask } from '../rask/client';
            import { getGreeting, recordVisit } from '../rask/messages';
            import type { Greeting } from '../rask/contracts';

            @Component({
              selector: 'app-root',
              templateUrl: './app.html',
              styleUrl: './app.css',
            })
            export class App {
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
            <main>
              <h1>Rask + Angular</h1>

              <label>
                Name
                <input [value]="name()" (input)="setName($any($event.target).value)" />
              </label>

              @if (!greeting() && !error()) {
                <p>Loading…</p>
              }

              @if (error(); as message) {
                <p role="alert">{{ message }}</p>
              }

              @if (greeting(); as data) {
                <p>{{ data.message }}</p>
                <p>Server time: {{ time(data.seenAt) }}</p>
                <p>Visits: {{ data.visits }}</p>
              }

              <button [disabled]="busy()" (click)="record()">Record a visit</button>
            </main>

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Lit =>
    [
        ("src/my-element.ts", """
            import { LitElement, html } from 'lit'
            import { customElement, state } from 'lit/decorators.js'
            import { rask } from './rask/client'
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

              private inFlight: AbortController | null = null

              connectedCallback() {
                super.connectedCallback()
                void this.load()
              }

              disconnectedCallback() {
                super.disconnectedCallback()
                this.inFlight?.abort()
              }

              render() {
                return html`
                  <main>
                    <h1>Rask + Lit</h1>

                    <label>
                      Name
                      <input
                        .value=${this.name}
                        @input=${(event: Event) => {
                          this.name = (event.target as HTMLInputElement).value
                          void this.load()
                        }}
                      />
                    </label>

                    ${!this.greeting && !this.error ? html`<p>Loading…</p>` : ''}
                    ${this.error ? html`<p role="alert">${this.error}</p>` : ''}
                    ${this.greeting
                      ? html`
                          <p>${this.greeting.message}</p>
                          <!-- seenAt is a real Date, revived because the C# type said so. -->
                          <p>
                            Server time:
                            ${new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(
                              this.greeting.seenAt,
                            )}
                          </p>
                          <p>Visits: ${this.greeting.visits}</p>
                        `
                      : ''}

                    <button ?disabled=${this.busy} @click=${this.record}>
                      Record a visit
                    </button>
                  </main>
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
