// Node-driven fixture for the SOLID adapter's children (src/Rask.External/client/solid.ts), against the REAL solid-js
// browser build and a real DOM (happy-dom). Solid never re-runs a component, so "keeps its state" here means the child
// component function ran ONCE and its signal survived every parent update.
//
// The C# test (SolidAdapterTests) runs this and asserts the JSON on stdout.

import {Window} from 'happy-dom'

const window = new Window({url: 'https://rask.test/'})
const document = window.document

globalThis.document = document as never
globalThis.window = window as never
globalThis.MutationObserver = window.MutationObserver as never

// Imported after the DOM globals exist: solid-js/web touches `document` at module evaluation in its browser build.
const {createSignal, onCleanup, onMount} = await import('solid-js')
const {insert} = await import('solid-js/web')
const {solidComponent} = await import('../../src/Rask.External/client/solid')

let effectRuns = 0
let badgeRuns = 0
let badgeCleanups = 0

// No JSX: the fixture is bundled by esbuild without Solid's compiler, so the DOM is built by hand, with `insert` — the
// call Solid's compiler emits for `{expression}` — doing the tracked reads.
function Counter(props: {heading?: string; children?: unknown}) {
    const [count, setCount] = createSignal(0)
    onMount(() => {
        effectRuns++
    })

    const root = document.createElement('div')
    const heading = root.appendChild(document.createElement('h2'))
    heading.id = 'heading'
    const counter = root.appendChild(document.createElement('span'))
    counter.id = 'count'
    const bump = root.appendChild(document.createElement('button'))
    bump.id = 'bump'
    bump.addEventListener('click', () => setCount((c) => c + 1))
    const slot = root.appendChild(document.createElement('div'))
    slot.id = 'slot'

    insert(heading as never, () => props.heading)
    insert(counter as never, () => String(count()))
    insert(slot as never, () => props.children as never)

    return root as never
}

function Badge(props: {label?: string}) {
    badgeRuns++
    const [clicks, setClicks] = createSignal(0)
    onCleanup(() => {
        badgeCleanups++
    })

    const button = document.createElement('button')
    button.id = 'badge'
    button.addEventListener('click', () => setClicks((c) => c + 1))
    insert(button as never, () => `${props.label}:${clicks()}`)
    return button as never
}

const chunks: Record<string, unknown> = {
    Chart: {default: solidComponent(Counter as never), component: Counter},
    Badge: {default: solidComponent(Badge as never), component: Badge},
}

const requested: string[] = []
const globals = globalThis as unknown as Record<string, unknown>
globals.__raskExternal = {
    resolve: (name: string) => {
        requested.push(name)
        return Promise.resolve(chunks[name])
    },
}
globals.__raskHost = {send: () => {}}
globals.__raskExternalManual = true

const runtime = await import('../../src/Rask.External/wwwroot/rask-external.js')

const settle = async () => {
    for (let turn = 0; turn < 8; turn++) {
        await new Promise((resolve) => setTimeout(resolve, 0))
    }
}
const text = (id: string) => document.querySelector('#' + id)?.textContent ?? null
const click = (id: string) => (document.querySelector('#' + id) as unknown as {click(): void}).click()
const withChild = (heading: string, label: string) =>
    JSON.stringify({heading, $c: ['Revenue ', {n: 'Badge', k: 'b1', p: {label}}]})

const island = document.createElement('rask-external')
island.setAttribute('name', 'Chart')
island.setAttribute('props', withChild('Costs', 'new'))
document.body.appendChild(island)

runtime.start(document)
await settle()
const slotWithChild = text('slot')

click('bump')
click('badge')
await settle()
const badgeAfterClick = text('badge')

island.setAttribute('props', withChild('Margin', 'newer'))
await settle()
const badgeAfterParentUpdate = text('badge')
const countAfterParentUpdate = text('count')
const badgeRunsAfterParentUpdate = badgeRuns

island.setAttribute('props', JSON.stringify({heading: 'Margin'}))
await settle()
const badgeGone = document.querySelector('#badge') === null
const badgeCleanupsAfterRemoval = badgeCleanups
const headingWithoutChild = text('heading')

runtime.__internals.unmount(island)
await settle()

process.stdout.write(JSON.stringify({
    slotWithChild,
    badgeAfterClick,
    badgeAfterParentUpdate,
    countAfterParentUpdate,
    badgeRunsAfterParentUpdate,
    effectRuns,
    badgeGone,
    badgeCleanupsAfterRemoval,
    headingWithoutChild,
    childRequests: requested.filter((name) => name === 'Badge').length,
}) + '\n')
