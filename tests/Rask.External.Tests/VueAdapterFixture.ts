// Node-driven fixture for the VUE adapter's children (src/Rask.External/client/vue.ts), against the REAL vue runtime
// and a real DOM (happy-dom). Children are the component's DEFAULT SLOT; the same run as the Preact and React fixtures.
//
// The C# test (VueAdapterTests) runs this and asserts the JSON on stdout.

import {Window} from 'happy-dom'

const window = new Window({url: 'https://rask.test/'})
const document = window.document

globalThis.document = document as never
globalThis.window = window as never
globalThis.MutationObserver = window.MutationObserver as never
globalThis.Element = window.Element as never
globalThis.SVGElement = window.SVGElement as never

// Imported after the DOM globals exist: @vue/runtime-dom reads `document` once, when it is evaluated, and a static
// import is hoisted above every assignment in this file — it would render into a document that was never there.
const {defineComponent, h, onMounted, onUnmounted, ref} = await import('vue')
const {vueComponent} = await import('../../src/Rask.External/client/vue')

let effectRuns = 0
let badgeEffects = 0
let badgeCleanups = 0

const Counter = defineComponent({
    props: {heading: String},
    setup(props, {slots}) {
        const count = ref(0)
        onMounted(() => {
            effectRuns++
        })

        return () => h('div', null, [
            h('h2', {id: 'heading'}, props.heading),
            h('span', {id: 'count'}, String(count.value)),
            h('button', {id: 'bump', onClick: () => count.value++}, 'bump'),
            h('div', {id: 'slot'}, slots.default?.()),
        ])
    },
})

const Badge = defineComponent({
    props: {label: String},
    setup(props) {
        const clicks = ref(0)
        onMounted(() => {
            badgeEffects++
        })
        onUnmounted(() => {
            badgeCleanups++
        })

        return () => h('button', {id: 'badge', onClick: () => clicks.value++}, `${props.label}:${clicks.value}`)
    },
})

const chunks: Record<string, unknown> = {
    Chart: {default: vueComponent(Counter), component: Counter},
    Badge: {default: vueComponent(Badge), component: Badge},
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
