// Node-driven fixture for the ANGULAR adapter (src/Rask.External/client/angular.ts), against the REAL @angular/core
// and @angular/platform-browser and a real DOM (happy-dom).
//
// The components are compiled by Angular's JIT compiler — `@angular/compiler` imported before anything declares a
// component, which is the documented way to run Angular with no build step. An app's islands are AOT-compiled by the
// Vite plugin instead; the adapter never sees the difference, because everything it touches (`createApplication`,
// `createComponent`, `setInput`, `reflectComponentType`) is runtime API that reads the same compiled definition either
// way. The components are declared by calling `Component({...})` on a class rather than with `@Component`, so the
// fixture needs no decorator transform from the bundler.
//
// What the adapter can get wrong, none of it visible from C#:
//   * props must arrive through `setInput` and repaint, though no Angular event handler wrote them;
//   * an update must keep the component instance — its own fields survive;
//   * a prop named `@alias` is an OUTPUT to subscribe to, and one C# stops sending must be unsubscribed;
//   * an input C# stops sending goes back to the component's own default;
//   * children are projected into `<ng-content>` and reconciled by key inside that one projected container;
//   * unmount must destroy the component (ngOnDestroy) and leave nothing in the island;
//   * and, alone among the adapters, the bootstrap is ASYNCHRONOUS: props that arrive while it is in flight must be
//     the ones rendered, and an island unmounted while it is in flight must destroy the application it is then handed.
//
// The C# test (AngularAdapterTests) runs this and asserts the JSON on stdout.

import {Window} from 'happy-dom'

const window = new Window({url: 'https://rask.test/'})
const document = window.document
const globals = globalThis as unknown as Record<string, unknown>

// Angular's DOM renderer and platform read these off the global object.
for (const name of ['window', 'document', 'Node', 'Element', 'HTMLElement', 'Text', 'Comment', 'DocumentFragment',
    'Event', 'CustomEvent', 'KeyboardEvent', 'MouseEvent', 'MutationObserver', 'navigator', 'location', 'history',
    'getComputedStyle', 'requestAnimationFrame', 'cancelAnimationFrame', 'CSSStyleSheet', 'ShadowRoot']) {
    Object.defineProperty(globals, name, {
        value: name === 'window' ? window : name === 'document' ? document : (window as unknown as Record<string, unknown>)[name],
        configurable: true,
        writable: true,
    })
}

// The JIT compiler, loaded for its side effect and before any component is declared. Everything Angular is imported
// after the DOM globals exist: a static import is hoisted above every assignment in this file.
await import('@angular/compiler')
const {ApplicationRef, Component, EventEmitter} = await import('@angular/core')
const {angularComponent} = await import('../../src/Rask.External/client/angular')

const stats = {created: 0, destroyed: 0, badgesCreated: 0, badgesDestroyed: 0, appsDestroyed: 0}

// Counted on the real class, so "the application was destroyed" is observed rather than inferred.
const destroyApp = ApplicationRef.prototype.destroy
ApplicationRef.prototype.destroy = function (this: InstanceType<typeof ApplicationRef>) {
    stats.appsDestroyed++
    return destroyApp.call(this)
}

// ----- the island components, as an author would write them -----

class CounterComponent {
    heading = 'untitled'
    // State the component owns and Rask knows nothing about. It is what a remount would destroy.
    count = 0
    readonly pointClick = new EventEmitter<number>()

    constructor() {
        stats.created++
    }

    ngOnDestroy() {
        stats.destroyed++
    }
}

Component({
    selector: 'fixture-counter',
    standalone: true,
    inputs: ['heading'],
    outputs: ['pointClick'],
    template: `
        <h2 id="heading">{{ heading }}</h2>
        <span id="count">{{ count }}</span>
        <button id="bump" (click)="count = count + 1">bump</button>
        <button id="ping" (click)="pointClick.emit(42)">ping</button>
        <div id="slot"><ng-content /></div>`,
})(CounterComponent)

// Rendered only as a CHILD island.
class BadgeComponent {
    label = ''
    clicks = 0

    constructor() {
        stats.badgesCreated++
    }

    ngOnDestroy() {
        stats.badgesDestroyed++
    }
}

Component({
    selector: 'fixture-badge',
    standalone: true,
    inputs: ['label'],
    template: `<button id="badge" (click)="clicks = clicks + 1">{{ label }}:{{ clicks }}</button>`,
})(BadgeComponent)

// What the generated entry modules do: the adapter as the default export, the component beside it.
const adapter = angularComponent(CounterComponent)
const chunks: Record<string, unknown> = {
    Chart: {default: adapter, component: CounterComponent},
    Badge: {default: angularComponent(BadgeComponent), component: BadgeComponent},
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

// Turns of the macrotask queue, as in the sibling fixtures — more of them, because Angular's bootstrap is a promise
// chain of its own and its change-detection scheduler races a timer against an animation frame.
const settle = async () => {
    for (let turn = 0; turn < 24; turn++) {
        await new Promise((resolve) => setTimeout(resolve, 0))
    }
}
const text = (id: string, root: {querySelector(selector: string): {textContent: string | null} | null} = document) =>
    root.querySelector('#' + id)?.textContent?.trim() ?? null
const click = (id: string) => (document.querySelector('#' + id) as unknown as {click(): void}).click()

const bootstrapErrors: string[] = []
const consoleError = console.error
console.error = (...parts: unknown[]) => {
    bootstrapErrors.push(parts.map(String).join(' '))
}

// ----- through the runtime -----

const island = document.createElement('rask-external')
island.setAttribute('name', 'Chart')
island.setAttribute('props', JSON.stringify({heading: 'Revenue', '@pointClick': {$h: 'c7:3'}}))
document.body.appendChild(island)

runtime.start(document)
await settle()

const headingOnMount = text('heading')
const createdOnMount = stats.created
const slotWithoutChildren = text('slot')

// State the component owns, moved off its initial value. Nothing in the props knows about it.
click('bump')
await settle()
const countAfterClick = text('count')

// A prop change, as a C# re-render delivers it: one attribute write and nothing else.
island.setAttribute('props', JSON.stringify({heading: 'Costs', '@pointClick': {$h: 'c7:3'}}))
await settle()
const headingAfterUpdate = text('heading')
const countAfterUpdate = text('count')
const createdAfterUpdate = stats.created
const destroyedAfterUpdate = stats.destroyed

// The output the server bound as {"$h": …}, emitted from inside the component.
click('ping')
await settle()
const dispatchedAfterCall = dispatched.length

// C# cleared the callback and stopped sending the heading: the output is unsubscribed and the input goes back to the
// component's own default.
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
const badgeInsideSlot = document.querySelector('#slot #badge') !== null

click('badge')
await settle()
const badgeAfterClick = text('badge')

island.setAttribute('props', withChild('Margin', 'newer'))
await settle()
const badgeAfterParentUpdate = text('badge')
const countAfterParentUpdate = text('count')
const badgesCreatedAfterParentUpdate = stats.badgesCreated
const createdAfterChildren = stats.created

island.setAttribute('props', JSON.stringify({heading: 'Margin'}))
await settle()
const badgeGone = document.querySelector('#badge') === null
const badgesDestroyedAfterRemoval = stats.badgesDestroyed
const headingWithoutChild = text('heading')

const appsDestroyedBeforeUnmount = stats.appsDestroyed
runtime.__internals.unmount(island)
await settle()
const destroyedAfterUnmount = stats.destroyed
const appsDestroyedByUnmount = stats.appsDestroyed - appsDestroyedBeforeUnmount
const islandEmptyAfterUnmount = island.childNodes.length === 0

// ----- the asynchronous bootstrap, driven on the adapter directly -----
//
// Through the runtime the window between mount() and the application existing cannot be held open from outside. On the
// adapter it can: everything below up to the first `await` runs in the turn mount() was called in, and no promise
// resolves inside a turn.

const early = document.createElement('div')
document.body.appendChild(early)
const earlyHandle = adapter.mount(early as never, {heading: 'First'})
const renderedBeforeBootstrap = early.querySelector('#heading') !== null
adapter.update!(earlyHandle, {heading: 'Second'})
adapter.update!(earlyHandle, {heading: 'Third'}, ['late child'])
await settle()
const headingAfterEarlyUpdate = text('heading', early as never)
const slotAfterEarlyUpdate = text('slot', early as never)
adapter.unmount!(earlyHandle)
await settle()

const createdBeforeAbandoned = stats.created
const appsDestroyedBeforeAbandoned = stats.appsDestroyed
const abandoned = document.createElement('div')
document.body.appendChild(abandoned)
const abandonedHandle = adapter.mount(abandoned as never, {heading: 'Never'})
adapter.unmount!(abandonedHandle)
const appsDestroyedSameTurn = stats.appsDestroyed - appsDestroyedBeforeAbandoned
await settle()
const componentsCreatedForAbandoned = stats.created - createdBeforeAbandoned
const appsDestroyedForAbandoned = stats.appsDestroyed - appsDestroyedBeforeAbandoned
const abandonedIsEmpty = abandoned.childNodes.length === 0

console.error = consoleError

process.stdout.write(JSON.stringify({
    bootstrapErrors,
    headingOnMount,
    createdOnMount,
    slotWithoutChildren,
    countAfterClick,
    headingAfterUpdate,
    countAfterUpdate,
    createdAfterUpdate,
    destroyedAfterUpdate,
    dispatched,
    dispatchedAfterCall,
    dispatchedAfterClear,
    headingAfterClear,
    slotWithChild,
    badgeInsideSlot,
    badgeAfterClick,
    badgeAfterParentUpdate,
    countAfterParentUpdate,
    badgesCreatedAfterParentUpdate,
    createdAfterChildren,
    badgeGone,
    badgesDestroyedAfterRemoval,
    headingWithoutChild,
    childRequests: requested.filter((name) => name === 'Badge').length,
    destroyedAfterUnmount,
    appsDestroyedByUnmount,
    islandEmptyAfterUnmount,
    renderedBeforeBootstrap,
    headingAfterEarlyUpdate,
    slotAfterEarlyUpdate,
    appsDestroyedSameTurn,
    componentsCreatedForAbandoned,
    appsDestroyedForAbandoned,
    abandonedIsEmpty,
}) + '\n')
