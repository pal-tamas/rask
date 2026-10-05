// The window the shared runtime (rask-api.ts) registers itself on, for WebRuntimeFixture: node's globalThis, with the
// events it listens for at load. Imported before the runtime, so it is there when the runtime evaluates.
const events = new EventTarget();
Object.assign(globalThis, {
    window: globalThis,
    // The page's nodes, which what crosses as data is told apart from: none in this fixture.
    Node: class {},
    Element: class {},
    addEventListener: events.addEventListener.bind(events),
    removeEventListener: events.removeEventListener.bind(events),
    dispatchEvent: events.dispatchEvent.bind(events),
});
