// Node-driven fixture: which cookie and which query key does `getCultureSignals` read?
//
// They are the app's RaskCultureOptions.CookieName and QueryKey, handed in by the host — not ASP.NET's
// defaults written into the script.
//
// Usage:  node CultureSignalsFixture.mjs <rask.wasm.js path> <cookie header> <query string> <cookie name> <query key>
import {readFileSync} from "node:fs";

const [bundlePath, cookies, search, cookieName, queryKey] = process.argv.slice(2);
if (!bundlePath || cookieName === undefined || queryKey === undefined) {
    console.error("usage: node CultureSignalsFixture.mjs <rask.wasm.js path> <cookies> <search> <cookie name> <query key>");
    process.exit(2);
}

const element = () => ({
    nodeType: 1,
    hasAttribute: () => false,
    getAttribute: () => null,
    setAttribute: () => { },
    removeAttribute: () => { },
    addEventListener: () => { },
    appendChild: () => { },
    querySelectorAll: () => [],
    contains: () => false,
});

const globals = globalThis as unknown as Record<string, unknown>;
globals.document = {
    head: element(),
    body: element(),
    documentElement: element(),
    cookie: cookies,
    getElementById: () => null,
    createElement: element,
    querySelector: () => null,
    querySelectorAll: () => [],
    addEventListener: () => { },
};
globals.window = globalThis;
globals.addEventListener = () => { };
globals.performance = {getEntriesByName: () => [], now: () => 0};
globals.location = {pathname: "/", search, origin: "http://localhost", href: "http://localhost/" + search};
globals.history = {replaceState: () => { }, pushState: () => { }};
globals.cancelAnimationFrame = () => { };
globals.requestAnimationFrame = () => 0;
globals.setInterval = () => 0;

const bundleSource = readFileSync(bundlePath, "utf8");
const mod = await import("data:text/javascript;base64," + btoa(unescape(encodeURIComponent(bundleSource)))) as {
    getCultureSignals(cookieName: string, queryKey: string): string;
};

process.stdout.write(mod.getCultureSignals(cookieName, queryKey) + "\n");
