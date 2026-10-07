// Node-driven fixture: what does `__raskHost.navigate(url, replace)` send to .NET?
//
// The generated `@rask/routes` module navigates through this bridge, so front-end island code can
// write `Routes.OrdersPage({ Page: 2 }).Go()` and never a path. It has to take the same road a click
// on an `a[data-rask-nav]` takes, and refuse a URL that is not this app's.
//
// Drives the built rask.wasm.js against a minimal stub DOM and prints one JSON line.
//
// Usage:  node HostNavigateFixture.mjs <rask.wasm.js path> <url> [replace]
import {readFileSync} from "node:fs";

const [bundlePath, target, replace] = process.argv.slice(2);
if (!bundlePath || !target) {
    console.error("usage: node HostNavigateFixture.mjs <rask.wasm.js path> <url> [replace]");
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
    getElementById: () => null,
    createElement: element,
    querySelector: () => null,
    querySelectorAll: () => [],
    addEventListener: () => { },
};
globals.window = globalThis;
globals.addEventListener = () => { };
globals.performance = {getEntriesByName: () => [], now: () => 0};
globals.location = {pathname: "/", search: "", origin: "http://localhost", href: "http://localhost/"};
globals.history = {replaceState: () => { }, pushState: () => { }};
globals.cancelAnimationFrame = () => { };
globals.requestAnimationFrame = () => 0;
globals.setInterval = () => 0;

const errors: string[] = [];
console.error = (...args: unknown[]) => void errors.push(args.map(String).join(" "));

const bundleSource = readFileSync(bundlePath, "utf8");
const mod = await import("data:text/javascript;base64," + btoa(unescape(encodeURIComponent(bundleSource)))) as {
    setExports(exports: unknown): void;
};

/** Every frame .NET was asked to dispatch. */
const frames: unknown[] = [];
mod.setExports({
    Rask: {
        Wasm: {
            JSInterop: {
                Dispatch: (bytes: Uint8Array) => void frames.push(JSON.parse(new TextDecoder().decode(bytes))),
                EndInvokeJSResult: () => { },
            },
        },
    },
});

const host = (globalThis as unknown as { __raskHost: { navigate(url: string, replace?: boolean): void } }).__raskHost;
host.navigate(target, replace === "replace");
await new Promise(resolve => setTimeout(resolve, 0));

process.stdout.write(JSON.stringify({frames, errors}) + "\n");
