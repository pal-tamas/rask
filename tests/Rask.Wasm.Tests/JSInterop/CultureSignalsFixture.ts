// Script fixture: which cookie and which query key does `getCultureSignals` read?
//
// They are the app's RaskCultureOptions.CookieName and QueryKey, handed in by the host — not ASP.NET's
// defaults written into the script.
//
// Arguments (ScriptFixture.Run):  <rask.wasm.js path> <cookie header> <query string> <cookie name> <query key>

import {args, report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";

const [bundlePath, cookies, search, cookieName, queryKey] = args;
if (!bundlePath || cookieName === undefined || queryKey === undefined) {
    throw new Error("CultureSignalsFixture takes: <rask.wasm.js path> <cookies> <search> <cookie name> <query key>");
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

const mod = await import(bundlePath) as {
    getCultureSignals(cookieName: string, queryKey: string): string;
};

report(JSON.parse(mod.getCultureSignals(cookieName, queryKey)));
