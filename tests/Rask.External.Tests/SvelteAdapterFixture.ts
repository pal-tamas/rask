// Node-driven fixture for the SVELTE adapter (src/Rask.External/client/svelte.svelte.ts, with the RaskHost and
// RaskTree components it mounts through), against the REAL svelte runtime and a real DOM (happy-dom).
//
// Svelte is the one adapter esbuild cannot bundle from source: `.svelte` files and the `$state` rune both need the
// Svelte compiler. SvelteFixtureCompile.mjs runs that compiler over the shipped adapter sources and the two fixture
// components beside this file, and the build aliases the result in as `rask-svelte-fixture/…` — so what runs here is
// the adapter as Svelte compiles it, not a stand-in.
//
// What the adapter can get wrong, none of it visible from C#:
//   * an update must write into the SAME `$state` props, so the component's own state survives — a remount is the only
//     other way to show new props, and it throws that state away;
//   * a callback C# stops sending must be DELETED from the props, or the stale one keeps firing;
//   * children reach the component as its `children` snippet, keyed, so a child keeps its instance across a parent
//     update and is destroyed when C# removes it;
//   * unmount must run the component's teardown and empty the island.
//
// The C# test (SvelteAdapterTests) runs this and asserts the JSON on stdout.

import {Window} from 'happy-dom'

const window = new Window({url: 'https://rask.test/'})
const document = window.document
const globals = globalThis as unknown as Record<string, unknown>

// Svelte reads its DOM prototypes off the globals the first time it mounts.
for (const name of ['window', 'document', 'Node', 'Element', 'HTMLElement', 'Text', 'Comment', 'DocumentFragment',
    'Event', 'CustomEvent', 'MutationObserver', 'navigator', 'requestAnimationFrame', 'cancelAnimationFrame']) {
    Object.defineProperty(globals, name, {
        value: name === 'window' ? window : name === 'document' ? document : (window as unknown as Record<string, unknown>)[name],
        configurable: true,
        writable: true,
    })
}

const stats = {effectRuns: 0, cleanupRuns: 0, badgeEffects: 0, badgeCleanups: 0}
globals.__svelteFixture = stats

// Imported after the DOM globals exist: a static import is hoisted above every assignment in this file.
const {svelteComponent} = await import('rask-svelte-fixture/svelte.svelte')
const {default: Counter} = await import('rask-svelte-fixture/FixtureCounter.svelte')
const {default: Badge} = await import('rask-svelte-fixture/FixtureBadge.svelte')

// What the generated entry modules do: the adapter as the default export, the component beside it.
const chunks: Record<string, unknown> = {
    Chart: {default: svelteComponent(Counter), component: Counter},
    Badge: {default: svelteComponent(Badge), component: Badge},
}

const requested: string[] = []
const dispatched: unknown[] = []
globals.__raskExternal = {
    resolve: (name: string) => {
        requested.push(name)
        return Promise.resolve(chunks[name])
    },
}
globals.__raskHost = {send: (payload: unknown) => dispatched.push(payload)}
globals.__raskExternalManual = true

const runtime = await import('../../src/Rask.External/wwwroot/rask-external.js')

const settle = async () => {
    for (let turn = 0; turn < 8; turn++) {
        await new Promise((resolve) => setTimeout(resolve, 0))
    }
}
const text = (id: string) => document.querySelector('#' + id)?.textContent ?? null
const click = (id: string) => (document.querySelector('#' + id) as unknown as {click(): void}).click()

const island = document.createElement('rask-external')
island.setAttribute('name', 'Chart')
island.setAttribute('props', JSON.stringify({heading: 'Revenue', onPointClick: {$h: 'c7:3'}}))
document.body.appendChild(island)

runtime.start(document)
await settle()

const headingOnMount = text('heading')
const effectsOnMount = stats.effectRuns
const slotWithoutChildren = text('slot')

// State the component owns, moved off its initial value. Nothing in the props knows about it.
click('bump')
await settle()
const countAfterClick = text('count')

// A prop change, as a C# re-render delivers it: one attribute write and nothing else.
island.setAttribute('props', JSON.stringify({heading: 'Costs', onPointClick: {$h: 'c7:3'}}))
await settle()
const headingAfterUpdate = text('heading')
const countAfterUpdate = text('count')
const effectsAfterUpdate = stats.effectRuns
const cleanupsAfterUpdate = stats.cleanupRuns

click('ping')
await settle()
const dispatchedAfterCall = dispatched.length

// C# cleared the callback and stopped sending the heading: both keys are simply absent from the next props.
island.setAttribute('props', JSON.stringify({}))
await settle()
click('ping')
await settle()
const dispatchedAfterClear = dispatched.length
const headingAfterClear = text('heading')

// ----- children -----

const withChild = (heading: string, label: string) =>
    JSON.stringify({heading, $c: ['Revenue ', {n: 'Badge', k: 'b1', p: {label}}]})

island.setAttribute('props', withChild('Costs', 'new'))
await settle()
const slotWithChild = text('slot')
const countAfterChildrenArrive = text('count')
const effectsAfterChildrenArrive = stats.effectRuns

click('badge')
await settle()
const badgeAfterClick = text('badge')

island.setAttribute('props', withChild('Margin', 'newer'))
await settle()
const badgeAfterParentUpdate = text('badge')
const countAfterParentUpdate = text('count')
const badgeEffectsAfterParentUpdate = stats.badgeEffects

island.setAttribute('props', JSON.stringify({heading: 'Margin'}))
await settle()
const badgeGone = document.querySelector('#badge') === null
const badgeCleanupsAfterRemoval = stats.badgeCleanups
const headingWithoutChild = text('heading')
const effectsAfterChildRemoval = stats.effectRuns

runtime.__internals.unmount(island)
await settle()

process.stdout.write(JSON.stringify({
    headingOnMount,
    effectsOnMount,
    slotWithoutChildren,
    countAfterClick,
    headingAfterUpdate,
    countAfterUpdate,
    effectsAfterUpdate,
    cleanupsAfterUpdate,
    dispatched,
    dispatchedAfterCall,
    dispatchedAfterClear,
    headingAfterClear,
    slotWithChild,
    countAfterChildrenArrive,
    effectsAfterChildrenArrive,
    badgeAfterClick,
    badgeAfterParentUpdate,
    countAfterParentUpdate,
    badgeEffectsAfterParentUpdate,
    badgeGone,
    badgeCleanupsAfterRemoval,
    headingWithoutChild,
    effectsAfterChildRemoval,
    childRequests: requested.filter((name) => name === 'Badge').length,
    cleanupsAfterUnmount: stats.cleanupRuns,
    islandEmptyAfterUnmount: island.childNodes.length === 0,
}) + '\n')
