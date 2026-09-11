// Node-driven fixture for the LIT half of the island boundary.
//
// Drives the runtime against the REAL adapter (src/Rask.External/client/lit.ts) and a real DOM (happy-dom), with a
// plain custom element standing in for a Lit one — the adapter touches nothing Lit-specific, only an element's
// properties and its events. Nothing between the `props` attribute and a dispatched event reaching the host is
// stubbed.
//
// What the adapter can get wrong, none of which any C# test can see:
//   * a prop named `@event` must become a LISTENER, not a property — an element's events are dispatched, and a
//     handler assigned to a property called "@sl-change" would sit there and never run;
//   * a re-render that keeps the handler must not add a second listener, or every event reaches C# twice;
//   * a handler C# replaced must stop firing, and one C# cleared must be removed;
//   * a prop C# stops sending must fall back to the element's own default — C# omits an unset prop rather than
//     sending null, and an element only re-renders from what is assigned to it;
//   * unmount must remove the element and everything listening on it.
//
// The C# test (LitAdapterTests) runs this and asserts the JSON on stdout.

import {Window} from 'happy-dom'
import {litComponent} from '../../src/Rask.External/client/lit'

// ----- the DOM -----

const window = new Window({url: 'https://rask.test/'})
const document = window.document

globalThis.document = document as never
globalThis.MutationObserver = window.MutationObserver as never

// ----- the element, as a package would register it -----

class FxDemo extends (window.HTMLElement as unknown as typeof HTMLElement) {
    /** A property with a default of its own, as a Lit element declares one. */
    tone = 'neutral'

    set label(value: string) {
        this.textContent = value
    }

    get label(): string {
        return this.textContent ?? ''
    }
}

window.customElements.define('fx-demo', FxDemo as never)

// What the generated entry module does for a package element named by its tag.
const adapter = litComponent('fx-demo')

// ----- the host the runtime expects -----

const dispatched: string[] = []
const globals = globalThis as unknown as Record<string, unknown>

globals.__raskExternal = {resolve: () => Promise.resolve({default: adapter})}
globals.__raskHost = {send: (payload: {id: string}) => dispatched.push(payload.id)}
globals.__raskExternalManual = true

const runtime = await import('../../src/Rask.External/wwwroot/rask-external.js')

// ----- the run -----

// The props exactly as the generator writes them: an unset prop is left out, and an event prop is a handler id with
// `$a` empty, because the event never crosses.
const props = (label: string, handler?: string, tone?: string) =>
    JSON.stringify({
        label,
        ...(tone ? {tone} : {}),
        ...(handler ? {'@fx-change': {$h: handler, $a: []}} : {}),
    })

const island = document.createElement('rask-external')
island.setAttribute('name', 'FxDemo')
island.setAttribute('module', 'fixture-lit/fx-demo.js#fx-demo')
island.setAttribute('props', props('Revenue', 'c7:3', 'danger'))
document.body.appendChild(island)

const settle = async () => {
    for (let turn = 0; turn < 4; turn++) {
        await new Promise((resolve) => setTimeout(resolve, 0))
    }
}
const element = () =>
    island.querySelector('fx-demo') as unknown as {label: string; tone: string; dispatchEvent(event: unknown): boolean} | null
const fire = () => element()?.dispatchEvent(new window.CustomEvent('fx-change', {detail: {checked: true}}))

runtime.start(document)
await settle()

const labelOnMount = element()?.label ?? null
const toneOnMount = element()?.tone ?? null
fire()
const afterMount = [...dispatched]

// A re-render that keeps the handler: the runtime revives the same function, so one listener stays one listener. It
// also stops sending `tone`, which has to go back to the element's own default rather than keep the last value.
island.setAttribute('props', props('Costs', 'c7:3'))
await settle()
const labelAfterUpdate = element()?.label ?? null
const toneAfterOmitted = element()?.tone ?? null
fire()
const afterSameHandler = [...dispatched]

// C# sets it again: the fallback is not a latch.
island.setAttribute('props', props('Costs', 'c7:3', 'danger'))
await settle()
const toneAfterResent = element()?.tone ?? null

// C# replaced the handler: only the new one may fire.
island.setAttribute('props', props('Costs', 'c7:4'))
await settle()
fire()
const afterNewHandler = [...dispatched]

// C# cleared it: nothing may fire.
island.setAttribute('props', props('Costs'))
await settle()
fire()
const afterClear = [...dispatched]

runtime.__internals.unmount(island)
await settle()

process.stdout.write(JSON.stringify({
    labelOnMount,
    labelAfterUpdate,
    toneOnMount,
    toneAfterOmitted,
    toneAfterResent,
    afterMount,
    afterSameHandler,
    afterNewHandler,
    afterClear,
    islandEmptyAfterUnmount: island.childNodes.length === 0,
}) + '\n')
