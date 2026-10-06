// Node-driven fixture for the client runtime's HYDRATION POLICIES
// (src/Rask.External/wwwroot/rask-external.js — schedule).
//
// `hydrate="none"` is covered by ExternalRuntimeFixture. This one covers the three policies that DO mount, and the
// one thing they share: an island that leaves before its moment came must never mount afterwards.
//
// The browser's two schedulers are stood in for by queues the fixture drains by hand, so "the browser went idle"
// and "the island scrolled into view" are statements in the run below rather than timings it hopes for. Both
// stand-ins honour their cancel — a callback that was cancelled is gone, an observer that was disconnected
// delivers nothing — because that is the contract the runtime's teardown relies on, and a stand-in that fired
// regardless would be testing a browser that does not exist.
//
// The C# test (ExternalHydrationTests) runs this and asserts the JSON on stdout.

type Callback = () => void

interface StubElement {
    nodeType: number
    tagName: string
    isConnected: boolean
    getAttribute(name: string): string | null
}

function makeIsland(name: string, hydrate: string | null): StubElement {
    const attributes = new Map<string, string>([['name', name], ['props', '{}']])
    if (hydrate !== null) {
        attributes.set('hydrate', hydrate)
    }

    return {
        nodeType: 1,
        tagName: 'RASK-EXTERNAL',
        isConnected: true,
        getAttribute: (attribute) => attributes.get(attribute) ?? null,
    }
}

const globals = globalThis as unknown as Record<string, unknown>

// ----- requestIdleCallback, drained by hand -----

const idleQueue = new Map<number, Callback>()
let idleSeq = 0
let idleCancelled = 0

const idleApi = {
    requestIdleCallback: (callback: Callback) => {
        idleQueue.set(++idleSeq, callback)
        return idleSeq
    },
    cancelIdleCallback: (handle: number) => {
        if (idleQueue.delete(handle)) {
            idleCancelled++
        }
    },
}

const goIdle = () => {
    const due = [...idleQueue.values()]
    idleQueue.clear()
    due.forEach((callback) => callback())
}

// ----- IntersectionObserver, fired by hand -----

interface StubObserver {
    callback: (entries: {target: unknown; isIntersecting: boolean}[]) => void
    targets: Set<unknown>
}

const intersectionObservers: StubObserver[] = []

class FakeIntersectionObserver implements StubObserver {
    readonly targets = new Set<unknown>()

    constructor(readonly callback: StubObserver['callback']) {
        intersectionObservers.push(this)
    }

    observe(target: unknown) {
        this.targets.add(target)
    }

    disconnect() {
        this.targets.clear()
    }
}

/** Reports `element` to whoever is still observing it. Returns how many observers that was. */
const intersect = (element: unknown, isIntersecting: boolean) => {
    const watching = intersectionObservers.filter((observer) => observer.targets.has(element))
    watching.forEach((observer) => observer.callback([{target: element, isIntersecting}]))
    return watching.length
}

// ----- the fake adapter and the resolver -----

const requested: string[] = []
const mountedNames: string[] = []

globals.document = {body: null}
globals.MutationObserver = class {
    observe() {}
    disconnect() {}
}
globals.__raskExternal = {
    resolve: (name: string) => {
        requested.push(name)
        return Promise.resolve({default: {mount: () => (mountedNames.push(name), {}), unmount: () => {}}})
    },
}
globals.__raskHost = {send: () => {}}
globals.__raskExternalManual = true

const runtime = await import('../../src/Rask.External/wwwroot/rask-external.js')
const {hydrate, unmount} = runtime.__internals as unknown as {
    hydrate(element: StubElement): void
    unmount(element: StubElement): void
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 0))
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))
const has = (list: string[], name: string) => list.includes(name)

const withBrowserApis = () => {
    Object.assign(globals, idleApi, {IntersectionObserver: FakeIntersectionObserver})
}
const withoutBrowserApis = () => {
    globals.requestIdleCallback = undefined
    globals.cancelIdleCallback = undefined
    globals.IntersectionObserver = undefined
}

withBrowserApis()

// ----- load: the default, and the explicit spelling of it -----

hydrate(makeIsland('Default', null))
hydrate(makeIsland('Load', 'load'))
await tick()
const defaultMounted = has(mountedNames, 'Default')
const loadMounted = has(mountedNames, 'Load')

// ----- idle -----

hydrate(makeIsland('Idle', 'idle'))
await tick()
const idleRequestedBeforeIdle = has(requested, 'Idle')
const idleMountedBeforeIdle = has(mountedNames, 'Idle')

goIdle()
await tick()
const idleMountedAfterIdle = has(mountedNames, 'Idle')

// ----- visible -----

const visible = makeIsland('Visible', 'visible')
hydrate(visible)
await tick()
const visibleObserved = intersectionObservers.some((observer) => observer.targets.has(visible))
const visibleRequestedOffscreen = has(requested, 'Visible')

// The observer reports every change, including the first "not intersecting" one a browser sends on observe().
intersect(visible, false)
await tick()
const visibleMountedOffscreen = has(mountedNames, 'Visible')

intersect(visible, true)
await tick()
const visibleMountedInView = has(mountedNames, 'Visible')

// Scrolled out and back in: the observer was disconnected at the first hit, so nothing is listening any more.
const visibleObserversAfterMount = intersect(visible, true)
await tick()
const visibleMounts = mountedNames.filter((name) => name === 'Visible').length

// ----- removed before the schedule fired -----

const idleGone = makeIsland('IdleGone', 'idle')
hydrate(idleGone)
await tick()
const cancelledBefore = idleCancelled
unmount(idleGone)
idleGone.isConnected = false
const idleCallbackCancelled = idleCancelled === cancelledBefore + 1
goIdle()
await tick()

const visibleGone = makeIsland('VisibleGone', 'visible')
hydrate(visibleGone)
await tick()
unmount(visibleGone)
visibleGone.isConnected = false
const visibleGoneObservers = intersect(visibleGone, true)
await tick()

// ----- a browser with neither API -----

withoutBrowserApis()

hydrate(makeIsland('IdleFallback', 'idle'))
const idleFallbackMountedSameTurn = has(requested, 'IdleFallback')
await wait(25)
const idleFallbackMounted = has(mountedNames, 'IdleFallback')

hydrate(makeIsland('VisibleFallback', 'visible'))
await tick()
const visibleFallbackMounted = has(mountedNames, 'VisibleFallback')

const idleFallbackGone = makeIsland('IdleFallbackGone', 'idle')
hydrate(idleFallbackGone)
unmount(idleFallbackGone)
idleFallbackGone.isConnected = false
await wait(25)

process.stdout.write(JSON.stringify({
    defaultMounted,
    loadMounted,
    idleRequestedBeforeIdle,
    idleMountedBeforeIdle,
    idleMountedAfterIdle,
    visibleObserved,
    visibleRequestedOffscreen,
    visibleMountedOffscreen,
    visibleMountedInView,
    visibleObserversAfterMount,
    visibleMounts,
    idleCallbackCancelled,
    visibleGoneObservers,
    idleFallbackMountedSameTurn,
    idleFallbackMounted,
    visibleFallbackMounted,
    requested,
    mounted: mountedNames,
}) + '\n')
