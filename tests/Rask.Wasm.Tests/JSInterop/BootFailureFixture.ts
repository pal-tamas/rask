// Drives the REAL src/Rask.Wasm/Browser/main.js under Node with a stub DOM, and reports what the
// boot screen ended up showing. Companion to BootFailureReportingTests.cs; see that file for why
// this is worth a Node subprocess rather than a C# assertion over the file's text.
//
// Usage:  node BootFailureFixture.mjs <path-to-main.js> <scenario>
//
// Scenarios — the three outcomes that have to be told apart:
//   runtime-fails    dotnet.create() rejects. The commonest real failure (a 404 on _framework, a
//                    wrong content type, an import-map/SRI drift) and the one #817 was opened for.
//   never-painted    every step resolves, but nothing ever morphed the document away.
//   already-painted  every step resolves and the app mounted. NEGATIVE CONTROL: the boot surface
//                    must stay silent, because painting an error over a live page would turn a
//                    working app into a broken-looking one.
import {mkdtempSync, mkdirSync, copyFileSync, writeFileSync} from "node:fs";
import {tmpdir} from "node:os";
import {join} from "node:path";
import {pathToFileURL} from "node:url";

const [mainJsPath, scenario] = process.argv.slice(2);

// --- stub DOM -------------------------------------------------------------------------------
//
// Deliberately a stub rather than jsdom: what is under test is what main.js paints when boot fails,
// and the three outcomes above are produced by controlling what `dotnet.create()` does — not by
// anything a real DOM would contribute.
interface StubElement {
    tagName: string;
    _children: StubElement[];
    textContent: string;
    className: string;
    style: Record<string, string>;
    hidden: boolean;
    isConnected: boolean;
    hasAttribute(name: string): boolean;
    getAttribute(name: string): string | null;
    setAttribute(name: string, value: string): void;
    appendChild(child: StubElement): StubElement;
    replaceChildren(...children: StubElement[]): void;
    querySelector(selector: string): StubElement | null;
}

function makeStubElement(tag: string): StubElement {
    const attrs = new Map<string, string>();
    const el: StubElement = {
        tagName: String(tag).toUpperCase(),
        _children: [],
        textContent: "",
        className: "",
        style: {},
        hidden: false,
        isConnected: true,
        hasAttribute: (name: string) => attrs.has(name),
        getAttribute: (name: string) => attrs.get(name) ?? null,
        setAttribute: (name: string, value: string) => void attrs.set(name, String(value)),
        appendChild: (child: StubElement) => {
            el._children.push(child);
            return child;
        },
        replaceChildren: (...children: StubElement[]) => {
            el._children = children;
        },
        querySelector: (sel: string) => el._children.find(c => `.${c.className}` === sel) ?? null,
    };
    return el;
}

/** The globals the boot module reaches for, which Node does not have. */
const globals = globalThis as unknown as {
    document: unknown;
    window: unknown;
    addEventListener: (type: string, fn: (event: unknown) => void) => void;
    __raskPainted?: boolean;
    __raskBootFailed?: (message: string, detail?: string) => void;
};

const boot = makeStubElement("div");
boot.className = "rask-boot";

const head = makeStubElement("head");
globals.document = {
    head,
    body: makeStubElement("body"),
    createElement: (tag: string) => makeStubElement(tag),
    querySelector: (sel: string) => (sel === ".rask-boot" ? boot : null),
    addEventListener: () => {
    }
};
globals.window = globalThis;

const listeners = new Map<string, ((event: unknown) => void)[]>();
globals.addEventListener = (type: string, fn: (event: unknown) => void) => {
    if (!listeners.has(type)) listeners.set(type, []);
    listeners.get(type)!.push(fn);
};

const consoleErrors: string[] = [];
const realError = console.error;
console.error = (...args: unknown[]) => {
    consoleErrors.push(args.map(String).join(" "));
};

// What "the app mounted" means: rask.wasm.js applied a frame and set __raskPainted. It does NOT mean the
// splash element went away — the morph patches the existing document in place, so the element main.js
// captured at import time is still connected afterwards.
//
// This fixture originally modelled mounting as `boot.isConnected = false`, which was an assumption about
// the morph that nothing had checked. main.js was written against that assumption and reported a boot
// failure for every successful boot; the browser gate caught it, the fixture did not, because the
// fixture encoded the same belief as the code. So the element deliberately stays connected here.
(globals as { __raskFixtureMounted?: () => void }).__raskFixtureMounted = () => {
    globals.__raskPainted = true;
};

// --- stub ./_framework/dotnet.js -----------------------------------------------------------
// main.js imports it by relative path, so it has to exist next to a copy of main.js.
const dir = mkdtempSync(join(tmpdir(), "rask-boot-"));
const mainCopy = join(dir, "main.js");
copyFileSync(mainJsPath, mainCopy);
mkdirSync(join(dir, "_framework"));

const createBody = scenario === "runtime-fails"
    ? `const e = new Error("Failed to fetch dotnet.native.wasm");
       e.stack = "Error: Failed to fetch dotnet.native.wasm\\n    at boot (_framework/dotnet.js:1:1)";
       return Promise.reject(e);`
    : `return Promise.resolve({
           getAssemblyExports: async () => ({}),
           runMain: async () => { ${scenario === "already-painted" ? "globalThis.__raskFixtureMounted();" : ""} return 0; }
       });`;

writeFileSync(join(dir, "_framework", "dotnet.js"), `
const chain = {
    withApplicationArgumentsFromQuery() { return chain; },
    withModuleConfig() { return chain; },
    create() { ${createBody} }
};
export const dotnet = chain;
`);

// main.js also imports ./rask.wasm.js once the runtime is up. The real one is a 4000-line bundle
// that wants a full DOM; only setExports is reached here, so stub that much.
writeFileSync(join(dir, "rask.wasm.js"), "export function setExports() {}\n");

let threw = false;
try {
    await import(pathToFileURL(mainCopy).href);
} catch (e) {
    // A failing boot reports and then RETHROWS — keeping the failure visible to the runtime's own
    // channels — so an exception here is part of the contract, not a fixture problem.
    threw = true;
}

const panel = boot._children[0];
const children = panel?._children ?? [];
const paragraphs = children.filter(c => c.tagName === "P");

process.stdout.write(JSON.stringify({
    scenario,
    threw,
    bootErrorAttributeSet: boot.hasAttribute("data-rask-boot-error"),
    heading: children.find(c => c.tagName === "H1")?.textContent ?? "",
    summary: paragraphs[0]?.textContent ?? "",
    detail: children.find(c => c.tagName === "PRE")?.textContent ?? "",
    styleInjected: head._children.some(c => c.tagName === "STYLE"),
    consoleErrors,
    unhandledRejectionHandlerRegistered: (listeners.get("unhandledrejection") ?? []).length > 0,
    errorHandlerRegistered: (listeners.get("error") ?? []).length > 0,
    bootFailedHookExposed: typeof globals.__raskBootFailed === "function"
}) + "\n");
console.error = realError;
