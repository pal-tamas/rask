// Node-driven fixture for the PREACT half of the island boundary.
//
// Where ExternalRuntimeFixture drives the runtime against a FAKE adapter — proving the runtime's own
// sequencing — this one drives the runtime against the REAL adapter
// (src/Rask.External/client/preact.ts), the REAL preact, and a real DOM (happy-dom). Nothing between
// the `props` attribute and a rendered `<h2>` is stubbed.
//
// Why this exists at all (#963). Preact shipped as one of the seven island runtimes with no sample
// and no browser coverage, because `@vitejs/plugin-react` resolves Babel 8 while
// `@preact/preset-vite` pins a `@babel/core@"7.x"` peer — npm refuses to install both, and both
// showcases already carry a React island. That refusal is correct behaviour and stays. It only blocks
// a bundled APP, though: a test fixture installs preact and nothing else, so the conflict never
// arises here.
//
// The four things the adapter can get wrong, none of which any C# test can see:
//   * mount must render into the island element itself, since that is the handle everything else uses;
//   * update must RECONCILE, not remount — `render()` into the same element is the whole update path,
//     and the proof is the component's own `useState` surviving a prop change it did not cause;
//   * a callback must reach the host dispatch channel, and must stop firing once C# clears it;
//   * unmount must be `render(null, element)`, which runs the tree's cleanup effects — dropping the
//     element instead leaks every effect still subscribed inside it, silently.
//
// The C# test (PreactAdapterTests) runs this and asserts the JSON on stdout.

import {Window} from 'happy-dom'
import {h, options} from 'preact'
import {useEffect, useState} from 'preact/hooks'
import {preactComponent} from '../../src/Rask.External/client/preact'

// ----- the DOM -----
//
// A real one, unlike the hand-written stub the runtime fixture uses. That stub is honest for the
// runtime, which touches five DOM methods; preact's renderer touches dozens, and a stub good enough
// to fool it would be a second implementation of the thing under test.

const window = new Window({url: 'https://rask.test/'})
const document = window.document

globalThis.document = document as never
globalThis.MutationObserver = window.MutationObserver as never

// preact's hooks defer effects behind a frame, falling back to a 100ms timer when there is no
// requestAnimationFrame. `options.requestAnimationFrame` is preact's own hook for exactly this, and it
// is the only one that works here: preact/hooks reads `typeof requestAnimationFrame` once, at module
// load, and a static import is hoisted above every assignment in this file — so putting the shim on
// globalThis would set it AFTER the module had already decided it was absent, and every wait below
// would silently have to outlast that 100ms fallback instead.
options.requestAnimationFrame = (callback: () => void) => setTimeout(callback, 0)

// ----- the island component, exactly as an author would write one -----

let effectRuns = 0
let cleanupRuns = 0

interface CounterProps {
    heading?: string
    onPointClick?: (value: number) => void
}

function Counter(props: CounterProps) {
    // State the component owns and Rask knows nothing about. It is what a remount would destroy.
    const [count, setCount] = useState(0)

    useEffect(() => {
        effectRuns++
        return () => {
            cleanupRuns++
        }
    }, [])

    return h('div', null, [
        h('h2', {id: 'heading'}, props.heading),
        h('span', {id: 'count'}, String(count)),
        h('button', {id: 'bump', onClick: () => setCount((c) => c + 1)}, 'bump'),
        h('button', {id: 'ping', onClick: () => props.onPointClick?.(42)}, 'ping'),
    ])
}

// What the generated entry module does, and the only Rask-aware line an island's front end ever gets.
const adapter = preactComponent(Counter)

// ----- the host the runtime expects -----

const requested: string[] = []
const dispatched: unknown[] = []
const globals = globalThis as unknown as Record<string, unknown>

globals.__raskExternal = {
    resolve: (name: string) => {
        requested.push(name)
        return Promise.resolve({default: adapter})
    },
}
globals.__raskHost = {send: (payload: unknown) => dispatched.push(payload)}

// Hold the runtime back so the fixture controls mount timing. Set before the import, which is why
// that import is dynamic: a static one is hoisted above every statement in the file.
globals.__raskExternalManual = true

const runtime = await import('../../src/Rask.External/wwwroot/rask-external.js')

// ----- the run -----

const island = document.createElement('rask-external')
island.setAttribute('name', 'Chart')
island.setAttribute('module', './Chart.tsx')
island.setAttribute('props', JSON.stringify({heading: 'Revenue', onPointClick: {$h: 'c7:3'}}))
document.body.appendChild(island)

// Counted in TURNS of the macrotask queue, not milliseconds. Mounting takes a microtask (the resolver
// is awaited), preact schedules the render on one turn, and its hooks defer effects across two more
// via requestAnimationFrame. A single `setTimeout(…, 5)` looks like it covers that and does not: it is
// scheduled first, so it can fire before hops queued after it, and the test then reads state the
// runtime was still about to produce.
const settle = async () => {
    for (let turn = 0; turn < 8; turn++) {
        await new Promise((resolve) => setTimeout(resolve, 0))
    }
}
const text = (id: string) => document.querySelector('#' + id)?.textContent ?? null
const click = (id: string) => (document.querySelector('#' + id) as unknown as {click(): void}).click()

runtime.start(document)
await settle()

const headingOnMount = text('heading')
const effectsOnMount = effectRuns

// State the component owns, moved off its initial value. Nothing in the props knows about it.
click('bump')
await settle()
const countAfterClick = text('count')

// A prop change, as a C# re-render delivers it: one attribute write and nothing else.
island.setAttribute('props', JSON.stringify({heading: 'Costs', onPointClick: {$h: 'c7:3'}}))
await settle()

const headingAfterUpdate = text('heading')
const countAfterUpdate = text('count')
const effectsAfterUpdate = effectRuns
const cleanupsAfterUpdate = cleanupRuns

// The callback the server sent as {"$h": …}, called from inside the component.
click('ping')
await settle()
const dispatchedAfterCall = dispatched.length

// C# cleared the callback. The prop is gone, so the button must now do nothing at all — a stale
// closure holding the previous render's function would keep dispatching.
island.setAttribute('props', JSON.stringify({heading: 'Costs'}))
await settle()
click('ping')
await settle()
const dispatchedAfterClear = dispatched.length

// Teardown, as the runtime performs it when the island leaves the document.
runtime.__internals.unmount(island)
await settle()

process.stdout.write(JSON.stringify({
    requested,
    headingOnMount,
    effectsOnMount,
    countAfterClick,
    headingAfterUpdate,
    countAfterUpdate,
    effectsAfterUpdate,
    cleanupsAfterUpdate,
    dispatched,
    dispatchedAfterCall,
    dispatchedAfterClear,
    cleanupsAfterUnmount: cleanupRuns,
    islandEmptyAfterUnmount: island.childNodes.length === 0,
}) + '\n')
