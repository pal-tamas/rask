// Node-driven fixture for rask-offline.ts — `data-rask-offline` on <html> while the page cannot reach its app.
//
// A window that keeps its listeners and a <html> that keeps its attributes, and a navigator the fixture turns offline
// by hand, as the browser would. The C# test (OfflineMarkClientTests) runs this in a node subprocess and asserts the
// JSON line.

const listeners: Record<string, (() => void)[]> = {};
const attributes = new Set<string>();
const page = globalThis as unknown as Record<string, unknown>;
const navigatorState = {onLine: true};
Object.defineProperty(globalThis, "navigator", {value: navigatorState, configurable: true});
page.window = {
    addEventListener(type: string, listener: () => void) { (listeners[type] ||= []).push(listener); },
};
page.document = {
    documentElement: {
        toggleAttribute(name: string, force?: boolean) { if (force) { attributes.add(name); } else { attributes.delete(name); } },
    },
};

const {connectionIs} = await import("../../../src/Rask.Core/Resources/rask-offline.js");
const marked = () => attributes.has("data-rask-offline");
const raise = (type: string) => (listeners[type] || []).forEach((listener) => listener());

const online = marked();
raise("offline");
const afterTheBrowserWentOffline = marked();
raise("online");
const afterItCameBack = marked();
connectionIs(true);
const whileTheConnectionIsDown = marked();
raise("offline");
raise("online");
const browserBackButSocketStillDown = marked();
connectionIs(false);
const afterTheSocketReopened = marked();

process.stdout.write(JSON.stringify({
    online,
    afterTheBrowserWentOffline,
    afterItCameBack,
    whileTheConnectionIsDown,
    browserBackButSocketStillDown,
    afterTheSocketReopened,
}) + "\n");
