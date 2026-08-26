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
/// </remarks>
internal static class SpaClientSources
{
    public static IReadOnlyList<(string Path, string Content)> React =>
    [
        ("src/main.tsx", """
            import { StrictMode } from 'react'
            import { createRoot } from 'react-dom/client'
            import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
            import { RouterProvider } from '@tanstack/react-router'
            import { raskRetry } from './rask/query'
            import { router } from './router'
            import './index.css'

            // TanStack's own defaults, on purpose: staleTime 0 and a five-minute garbage-collection
            // window. Rask's C# query client mirrors them, so the two halves of an app behave the same
            // way. The one override is retry — a 4xx will never succeed on a retry, and the default of
            // three turns one refused request into four while telling the user nothing for seconds.
            const queryClient = new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })

            createRoot(document.getElementById('root')!).render(
              <StrictMode>
                <QueryClientProvider client={queryClient}>
                  <RouterProvider router={router} />
                </QueryClientProvider>
              </StrictMode>,
            )

            """),

        ("src/router.tsx", """
            import { Outlet, createRootRoute, createRoute, createRouter } from '@tanstack/react-router'
            import Home from './App'

            // Routes in code rather than through the file-based plugin. Two routes do not need a
            // generated route tree, and that plugin wants to own src/routes/ — which is the one thing
            // this template cannot give it, because the client is scaffolded by somebody else and Rask
            // only overlays.
            const rootRoute = createRootRoute({ component: () => <Outlet /> })

            const homeRoute = createRoute({
              getParentRoute: () => rootRoute,
              path: '/',
              component: Home,
            })

            export const router = createRouter({ routeTree: rootRoute.addChildren([homeRoute]) })

            // What makes Link, useNavigate and useParams know these routes by name rather than by string.
            declare module '@tanstack/react-router' {
              interface Register {
                router: typeof router
              }
            }

            """),

        ("src/App.tsx", """
            import { useState } from 'react'
            import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
            import { raskMutation, raskQuery } from './rask/query'
            import { getGreeting, recordVisit } from './rask/messages'
            import './App.css'

            export default function App() {
              const [name, setName] = useState('world')
              const queryClient = useQueryClient()

              // The message carries its own result type, so `greeting` is a Greeting with no cast and no
              // wire name spelled out here. Renaming a property in the C# record breaks this line at
              // build time rather than on the wire.
              const { data: greeting, isPending, error } = useQuery(raskQuery(getGreeting({ name })))

              const visit = useMutation({
                ...raskMutation(recordVisit),
                // The factory carries its wire name, so invalidation is never a string literal either.
                onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
              })

              return (
                <main>
                  <h1>Rask + React</h1>

                  <label>
                    Name <input value={name} onChange={(event) => setName(event.target.value)} />
                  </label>

                  {isPending && <p>Loading…</p>}
                  {error && <p role="alert">{error.message}</p>}

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

                  <button onClick={() => visit.mutate({ name })} disabled={visit.isPending}>
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
            import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
            import { raskRetry } from './rask/query'
            import { App } from './app.tsx'
            import './index.css'

            // @tanstack/react-query, not a preact-specific package: there is no such thing, and there
            // does not need to be. create-vite's Preact template already maps react and react-dom to
            // preact/compat in tsconfig.app.json, and @preact/preset-vite does the same at build time —
            // so the React adapter type-checks and bundles here unchanged.
            const queryClient = new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })

            render(
              <QueryClientProvider client={queryClient}>
                <App />
              </QueryClientProvider>,
              document.getElementById('app')!,
            )

            """),

        ("src/app.tsx", """
            import { useState } from 'preact/hooks'
            import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
            import { raskMutation, raskQuery } from './rask/query'
            import { getGreeting, recordVisit } from './rask/messages'
            import './app.css'

            export function App() {
              const [name, setName] = useState('world')
              const queryClient = useQueryClient()

              // The message carries its own result type, so `greeting` is a Greeting with no cast and no
              // wire name spelled out here. Renaming a property in the C# record breaks this line at
              // build time rather than on the wire.
              const { data: greeting, isPending, error } = useQuery(raskQuery(getGreeting({ name })))

              const visit = useMutation({
                ...raskMutation(recordVisit),
                // The factory carries its wire name, so invalidation is never a string literal either.
                onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
              })

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

                  {isPending && <p>Loading…</p>}
                  {error && <p role="alert">{error.message}</p>}

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

                  <button onClick={() => visit.mutate({ name })} disabled={visit.isPending}>
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
            import { QueryClient, QueryClientProvider } from '@tanstack/solid-query'
            import { RouterProvider } from '@tanstack/solid-router'
            import { raskRetry } from './rask/query'
            import { router } from './router'
            import './index.css'

            const queryClient = new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })

            render(
              () => (
                <QueryClientProvider client={queryClient}>
                  <RouterProvider router={router} />
                </QueryClientProvider>
              ),
              document.getElementById('root')!,
            )

            """),

        ("src/router.tsx", """
            import { Outlet, createRootRoute, createRoute, createRouter } from '@tanstack/solid-router'
            import Home from './App'

            // Routes in code rather than through the file-based plugin — see the React template's
            // router for why: that plugin wants to own src/routes/, and this client is scaffolded by
            // somebody else.
            const rootRoute = createRootRoute({ component: () => <Outlet /> })

            const homeRoute = createRoute({
              getParentRoute: () => rootRoute,
              path: '/',
              component: Home,
            })

            export const router = createRouter({ routeTree: rootRoute.addChildren([homeRoute]) })

            declare module '@tanstack/solid-router' {
              interface Register {
                router: typeof router
              }
            }

            """),

        ("src/App.tsx", """
            import { Show, createSignal } from 'solid-js'
            import { useMutation, useQuery, useQueryClient } from '@tanstack/solid-query'
            import { raskMutation, raskQuery } from './rask/query'
            import { getGreeting, recordVisit } from './rask/messages'
            import './App.css'

            export default function App() {
              const [name, setName] = createSignal('world')
              const queryClient = useQueryClient()

              // Solid's primitives take a FUNCTION returning options, and that is not a formality: it is
              // what lets the query re-read the signal and refetch when it changes. Passing the object
              // directly would read name() once, at setup, and never again.
              const greeting = useQuery(() => raskQuery(getGreeting({ name: name() })))

              const visit = useMutation(() => ({
                ...raskMutation(recordVisit),
                // The factory carries its wire name, so invalidation is never a string literal.
                onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
              }))

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

                  <Show when={greeting.isPending}>
                    <p>Loading…</p>
                  </Show>
                  <Show when={greeting.error}>
                    {(error) => <p role="alert">{error().message}</p>}
                  </Show>

                  <Show when={greeting.data}>
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

                  <button onClick={() => visit.mutate({ name: name() })} disabled={visit.isPending}>
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
            import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
            import { raskRetry } from './rask/query'
            import './style.css'
            import App from './App.vue'

            const queryClient = new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })

            createApp(App).use(VueQueryPlugin, { queryClient }).mount('#app')

            """),

        ("src/App.vue", """
            <script setup lang="ts">
            import { computed, ref } from 'vue'
            import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
            import { raskMutation, raskQuery } from './rask/query'
            import { getGreeting, recordVisit } from './rask/messages'

            const name = ref('world')
            const queryClient = useQueryClient()

            // A computed, not a plain object: that is what lets the options re-read the ref and refetch
            // when it changes. Passing the object directly would read name.value once, at setup.
            const { data: greeting, isPending, error } = useQuery(
              computed(() => raskQuery(getGreeting({ name: name.value }))),
            )

            const visit = useMutation({
              ...raskMutation(recordVisit),
              // The factory carries its wire name, so invalidation is never a string literal.
              onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
            })

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

                <p v-if="isPending">Loading…</p>
                <p v-else-if="error" role="alert">{{ error.message }}</p>

                <template v-if="greeting">
                  <p>{{ greeting.message }}</p>
                  <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
                  <p>Server time: {{ serverTime }}</p>
                  <p>Visits: {{ greeting.visits }}</p>
                </template>

                <button :disabled="visit.isPending.value" @click="visit.mutate({ name })">
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
              import { QueryClient, QueryClientProvider } from '@tanstack/svelte-query'
              import { raskRetry } from './rask/query'
              import Greeting from './lib/Greeting.svelte'

              const queryClient = new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })
            </script>

            <QueryClientProvider client={queryClient}>
              <Greeting />
            </QueryClientProvider>

            """),

        ("src/lib/Greeting.svelte", """
            <script lang="ts">
              import { createMutation, createQuery, useQueryClient } from '@tanstack/svelte-query'
              import { raskMutation, raskQuery } from '../rask/query'
              import { getGreeting, recordVisit } from '../rask/messages'

              let name = $state('world')
              const queryClient = useQueryClient()

              // Svelte Query v6 takes a THUNK, and that is not a formality: it is what lets the options
              // re-read the rune and refetch when it changes. Passing the object directly would read
              // `name` once, at setup, and never again.
              const greeting = createQuery(() => raskQuery(getGreeting({ name })))

              const visit = createMutation(() => ({
                ...raskMutation(recordVisit),
                // The factory carries its wire name, so invalidation is never a string literal.
                onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
              }))
            </script>

            <main>
              <h1>Rask + Svelte</h1>

              <label>
                Name <input bind:value={name} />
              </label>

              {#if greeting.isPending}
                <p>Loading…</p>
              {:else if greeting.isError}
                <p role="alert">{greeting.error.message}</p>
              {:else if greeting.data}
                <p>{greeting.data.message}</p>
                <!-- seenAt is a real Date, revived because the C# type said it was an instant. -->
                <p>
                  Server time:
                  {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.data.seenAt)}
                </p>
                <p>Visits: {greeting.data.visits}</p>
              {/if}

              <button onclick={() => visit.mutate({ name })} disabled={visit.isPending}>
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
            import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

            import { routes } from './app.routes';
            import { raskRetry } from '../rask/query';

            export const appConfig: ApplicationConfig = {
              providers: [
                provideBrowserGlobalErrorListeners(),
                provideRouter(routes),
                // TanStack's own defaults, on purpose — staleTime 0 and a five-minute collection window.
                // The one override is retry: a 4xx will never succeed on a retry.
                provideTanStackQuery(new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })),
              ],
            };

            """),

        ("src/app/app.ts", """
            import { Component, inject, signal } from '@angular/core';
            import { QueryClient, injectMutation, injectQuery } from '@tanstack/angular-query-experimental';

            import { raskMutation, raskQuery } from '../rask/query';
            import { getGreeting, recordVisit } from '../rask/messages';

            @Component({
              selector: 'app-root',
              templateUrl: './app.html',
              styleUrl: './app.css',
            })
            export class App {
              private readonly queryClient = inject(QueryClient);

              protected readonly name = signal('world');

              // injectQuery runs this function in a reactive context, so reading the signal here is what
              // makes the query refetch when the name changes — the same role the thunk plays in Solid.
              // The message carries its own result type, so `data()` is a Greeting with no cast and no wire
              // name spelled out here.
              protected readonly greeting = injectQuery(() => raskQuery(getGreeting({ name: this.name() })));

              protected readonly visit = injectMutation(() => ({
                ...raskMutation(recordVisit),
                // The factory carries its wire name, so invalidation is never a string literal.
                onSuccess: () => this.queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
              }));

              protected setName(value: string): void {
                this.name.set(value);
              }

              protected record(): void {
                this.visit.mutate({ name: this.name() });
              }

              // seenAt is a real Date, revived because the C# type said it was an instant. `undefined` as
              // the locale means the visitor's own, and their own time zone.
              protected time(value: Date): string {
                return new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(value);
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

              @if (greeting.isPending()) {
                <p>Loading…</p>
              }

              @if (greeting.error(); as error) {
                <p role="alert">{{ error.message }}</p>
              }

              @if (greeting.data(); as data) {
                <p>{{ data.message }}</p>
                <p>Server time: {{ time(data.seenAt) }}</p>
                <p>Visits: {{ data.visits }}</p>
              }

              <button [disabled]="visit.isPending()" (click)="record()">Record a visit</button>
            </main>

            """),
    ];

    public static IReadOnlyList<(string Path, string Content)> Lit =>
    [
        ("src/my-element.ts", """
            import { LitElement, html } from 'lit'
            import { customElement, state } from 'lit/decorators.js'
            import {
              QueryClient,
              QueryClientProvider,
              createMutationController,
              createQueryController,
            } from '@tanstack/lit-query'
            import { raskMutation, raskQuery, raskRetry } from './rask/query'
            import { getGreeting, recordVisit } from './rask/messages'

            const queryClient = new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })

            // The provider is a custom ELEMENT, and it has to be a DOM ancestor of anything that
            // queries — lit-query hands the client down through @lit/context, which travels by event up
            // the tree rather than by import. That is why <rask-greeting> is nested rather than mounted
            // on its own.
            @customElement('rask-query-provider')
            export class RaskQueryProvider extends QueryClientProvider {
              constructor() {
                super()
                this.client = queryClient
              }
            }

            @customElement('rask-greeting')
            export class RaskGreeting extends LitElement {
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

              // A thunk, so the controller re-reads the reactive property and refetches when it changes.
              private readonly greeting = createQueryController(this, () =>
                raskQuery(getGreeting({ name: this.name })),
              )

              private readonly visit = createMutationController(this, () => ({
                ...raskMutation(recordVisit),
                // The factory carries its wire name, so invalidation is never a string literal.
                onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
              }))

              render() {
                const query = this.greeting()
                const mutation = this.visit()

                return html`
                  <main>
                    <h1>Rask + Lit</h1>

                    <label>
                      Name
                      <input
                        .value=${this.name}
                        @input=${(event: Event) => {
                          this.name = (event.target as HTMLInputElement).value
                        }}
                      />
                    </label>

                    ${query.isPending ? html`<p>Loading…</p>` : ''}
                    ${query.isError ? html`<p role="alert">${query.error.message}</p>` : ''}
                    ${query.data
                      ? html`
                          <p>${query.data.message}</p>
                          <!-- seenAt is a real Date, revived because the C# type said so. -->
                          <p>
                            Server time:
                            ${new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(
                              query.data.seenAt,
                            )}
                          </p>
                          <p>Visits: ${query.data.visits}</p>
                        `
                      : ''}

                    <button
                      ?disabled=${mutation.isPending}
                      @click=${() => this.visit.mutate({ name: this.name })}
                    >
                      Record a visit
                    </button>
                  </main>
                `
              }
            }

            @customElement('my-element')
            export class MyElement extends LitElement {
              render() {
                return html`<rask-query-provider><rask-greeting></rask-greeting></rask-query-provider>`
              }
            }

            declare global {
              interface HTMLElementTagNameMap {
                'rask-query-provider': RaskQueryProvider
                'rask-greeting': RaskGreeting
                'my-element': MyElement
              }
            }

            """),
    ];
}
