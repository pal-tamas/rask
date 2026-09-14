// Node-driven fixture for the WASM manifest injector, `window.__raskPwa.applyManifest`
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
// The C# test (ManifestThemeColorTests) runs this in a node subprocess and asserts the JSON line.
import {readFileSync} from "node:fs";

const bundlePath = process.argv[2];
if (!bundlePath) {
    console.error("usage: node ManifestThemeColorFixture.mjs <rask.wasm.js path>");
    process.exit(2);
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
    documentElement: {tagName: "HTML", removeAttribute: () => { }},
    getElementById: () => null,
    createElement: (tag: string) => el(tag),
    querySelector,
    addEventListener: () => { },
};
globals.window = globalThis;
globals.addEventListener = () => { };
globals.performance = {getEntriesByName: () => []};
globals.location = {pathname: "/", search: ""};
globals.history = {replaceState: () => { }, pushState: () => { }};
globals.requestAnimationFrame = () => 0;
globals.cancelAnimationFrame = () => { };

const bundleSource = readFileSync(bundlePath, "utf8");
await import("data:text/javascript;base64," + btoa(unescape(encodeURIComponent(bundleSource))));

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

console.log(JSON.stringify({declared, manifestLinked, undeclared, pair}));
