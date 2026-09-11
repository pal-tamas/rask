// Node-driven fixture for the REACT adapter's children (src/Rask.External/client/react.ts), against the REAL react,
// react-dom and a real DOM (happy-dom). The same run as PreactAdapterFixture's children, so a difference between the
// two adapters shows up as the same assertion failing for one of them.
//
// What the adapter can get wrong here, none of which any C# test can see:
//   * a child island must render inside its parent's tree — one root, so the tree reconciles together;
//   * a parent update must keep the child's own state (same component, same key), not remount it;
//   * a child C# removes must unmount with its cleanup, and the parent must stay.
//
// The C# test (ReactAdapterTests) runs this and asserts the JSON on stdout.

import {Window} from 'happy-dom'
import type {ReactNode} from 'react'

const window = new Window({url: 'https://rask.test/'})
const document = window.document

globalThis.document = document as never
globalThis.window = window as never
globalThis.MutationObserver = window.MutationObserver as never
globalThis.HTMLElement = window.HTMLElement as never

// Imported after the DOM globals exist: react-dom decides whether it can use the DOM once, when it is evaluated, and a
// static import is hoisted above every assignment in this file.
const {createElement, useEffect, useState} = await import('react')
const {reactComponent} = await import('../../src/Rask.External/client/react')

let effectRuns = 0
let badgeEffects = 0
let badgeCleanups = 0

function Counter(props: {heading?: string; children?: ReactNode}) {
    const [count, setCount] = useState(0)

    useEffect(() => {
        effectRuns++
    }, [])

    return createElement('div', null,
        createElement('h2', {id: 'heading'}, props.heading),
        createElement('span', {id: 'count'}, String(count)),
        createElement('button', {id: 'bump', onClick: () => setCount((c) => c + 1)}, 'bump'),
        createElement('div', {id: 'slot'}, props.children))
}

function Badge(props: {label?: string}) {
    const [clicks, setClicks] = useState(0)

    useEffect(() => {
        badgeEffects++
        return () => {
            badgeCleanups++
        }
    }, [])

    return createElement('button', {id: 'badge', onClick: () => setClicks((c) => c + 1)}, `${props.label}:${clicks}`)
}

const chunks: Record<string, unknown> = {
    Chart: {default: reactComponent(Counter), component: Counter},
    Badge: {default: reactComponent(Badge), component: Badge},
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

// React's scheduler posts work across macrotasks; counted in turns, like the Preact fixture.
const settle = async () => {
    for (let turn = 0; turn < 12; turn++) {
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
const badgeEffectsAfterParentUpdate = badgeEffects

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
    badgeEffectsAfterParentUpdate,
    effectRuns,
    badgeGone,
    badgeCleanupsAfterRemoval,
    headingWithoutChild,
    childRequests: requested.filter((name) => name === 'Badge').length,
}) + '\n')
