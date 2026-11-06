// Script fixture for the WASM manifest injector, `window.__raskPwa.applyManifest`
// (Rask.Wasm/Resources/rask-wasm-api.ts).
//
// The symptom it guards: rask.sh's browser toolbar tint blinked on every boot. The page's head declares
// `<meta name="theme-color" content="#7c3aed">`; `UsePwa` configured a manifest whose theme_color was
// `#512BD4`; and the injector, run after the first render, OVERWROTE the page's tag with the manifest's
// colour. The next head morph put the page's value back ~25ms later. Safari and Chrome on Android paint
// that value into the tab bar, so the reader saw the chrome flash between two purples.
//
// Three heads, one call each, and the state of every theme-color tag afterwards:
//   * the page declares one            -> left exactly as the page wrote it
//   * the page declares none           -> the manifest's colour is added as the fallback
//   * the page declares a light/dark pair -> both left alone (the old code rewrote the first)
//
// The C# test (ManifestThemeColorTests) runs this on Jint and asserts what it reports.
import {args, report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";

const bundlePath = args[0];
if (!bundlePath) {
    throw new Error("ManifestThemeColorFixture takes: <rask.wasm.js path>");
}

interface StubElement {
    tagName: string;
    rel: string;
    href: string;
    name: string;
    content: string;
    media: string;
    parentNode: StubElement | null;
    _children: StubElement[];
    appendChild(child: StubElement): StubElement;
}

function el(tagName: string, props: Partial<StubElement> = {}): StubElement {
    const e: StubElement = {
        tagName: tagName.toUpperCase(),
        rel: "",
        href: "",
        name: "",
        content: "",
        media: "",
        parentNode: null,
        _children: [],
        appendChild: (child: StubElement) => {
            e._children.push(child);
            child.parentNode = e;
            return child;
        },
        ...props,
    };
    return e;
}

let head = el("head");

// <html> as far as the runtime touches it: it marks it data-rask-busy while it boots (rask-owned.ts busy()).
function html(): object {
    const attrs = new Map<string, string>();
    return {
        tagName: "HTML",
        hasAttribute: (name: string) => attrs.has(name),
        getAttribute: (name: string) => attrs.get(name) ?? null,
        setAttribute: (name: string, value: string) => void attrs.set(name, String(value)),
        removeAttribute: (name: string) => void attrs.delete(name),
    };
}

// Only the two selectors the injector asks for, matched literally: a stub that answered anything else
// would be testing itself.
function querySelector(selector: string): StubElement | null {
    if (selector === 'link[rel="manifest"]') {
        return head._children.find(c => c.tagName === "LINK" && c.rel === "manifest") ?? null;
    }
    if (selector === 'meta[name="theme-color"]') {
        return head._children.find(c => c.tagName === "META" && c.name === "theme-color") ?? null;
    }
    return null;
}

const globals = globalThis as unknown as Record<string, unknown>;
globals.document = {
    get head() {
        return head;
    },
    body: el("body"),
    baseURI: "https://example.com/",
    documentElement: html(),
    getElementById: () => null,
    createElement: (tag: string) => el(tag),
    querySelector,
    // No element of these pages carries `autofocus`, which the runtime asks about after every frame.
    querySelectorAll: () => [],
    addEventListener: () => { },
};
globals.window = globalThis;
globals.addEventListener = () => { };
globals.performance = {getEntriesByName: () => []};
globals.location = {pathname: "/", search: ""};
globals.history = {replaceState: () => { }, pushState: () => { }};
globals.requestAnimationFrame = () => 0;
globals.cancelAnimationFrame = () => { };

await import(bundlePath);

const pwa = (globalThis as unknown as { __raskPwa: { applyManifest(json: string): void } }).__raskPwa;
const manifest = JSON.stringify({name: "App", start_url: "/", theme_color: "#512BD4"});

function themeColors() {
    return head._children
        .filter(c => c.tagName === "META" && c.name === "theme-color")
        .map(c => ({content: c.content, media: c.media}));
}

head = el("head");
head.appendChild(el("meta", {name: "theme-color", content: "#7c3aed"}));
pwa.applyManifest(manifest);
const declared = themeColors();
const manifestLinked = head._children.some(c => c.tagName === "LINK" && c.rel === "manifest"
    && c.href.startsWith("data:application/manifest+json,"));

head = el("head");
pwa.applyManifest(manifest);
const undeclared = themeColors();

head = el("head");
head.appendChild(el("meta", {name: "theme-color", content: "#ffffff", media: "(prefers-color-scheme: light)"}));
head.appendChild(el("meta", {name: "theme-color", content: "#000000", media: "(prefers-color-scheme: dark)"}));
pwa.applyManifest(manifest);
const pair = themeColors();

report({declared, manifestLinked, undeclared, pair});
