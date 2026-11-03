// The devtools' place on the page: a corner pill that opens a drawer docked to the bottom or the right edge.
//
// Everything lives in an open shadow root on a <rask-devtools> element appended to <html>, after <body>:
//   * the app's stylesheets cannot reach inside it, whatever they select,
//   * data-rask-managed keeps the morph from removing it on a full frame,
//   * <body> keeps its place among <html>'s children, which the diff's positional paths walk through.
// DOM APIs only, never innerHTML, so a strict Content-Security-Policy changes nothing. Nothing covers the page until
// the drawer is opened, and the panel iframe is not created until then either.
//
// The pill sits bottom-LEFT: bottom-right is the hot-reload pill's, and the dev-error panel spans the bottom centre.

import {followPageScheme} from "./scheme.js";

export type Dock = "bottom" | "right";

export const DOCK_STORAGE_KEY = "rask.devtools.dock";

declare global {
    interface Window {
        /** Present once the devtools host has run in this document, whichever host loaded it. */
        __raskDevtoolsHost?: { readonly version: 1; readonly dock: DockHandle };
    }
}

export interface DockOptions {
    /** The panel page the drawer frames: a Server host's panel URL, or null when the panel is not a page. */
    readonly panelUrl: string | null;
    /** The panel frame's document, for a host whose panel is not a page (WASM). Used when there is no panel URL. */
    readonly frameDocument?: string | null;
    /** Called once, when the drawer first opens and its frame has been attached. */
    readonly onFrame?: (frame: HTMLIFrameElement) => void;
    /** Called whenever the drawer closes, however it was closed. */
    readonly onClose?: () => void;
}

export interface DockHandle {
    readonly element: HTMLElement;
    /** The devtools' shadow root, where the page overlays are drawn beside the pill and the drawer. */
    readonly shadow: ShadowRoot;
    isOpen(): boolean;
    toggle(): void;
    /** Creates the panel frame without opening the drawer, so the panel runs while it is shut. */
    preload(): void;
    /** Shows `count` unseen errors as a red dot on the pill while the drawer is shut; 0 takes the dot away. */
    setAlert(count: number): void;
    side(): Dock;
    setSide(side: Dock): void;
}

// Flux UI's look, measured from fluxui.dev's own buttons and badges (2026-11-02) and written out here, because the kit's
// stylesheet may not be on the page the dock is drawn in. The colours read the kit's own variables first, which reach
// into a shadow root by inheritance, so an app that re-points its zinc scale or its accent re-skins the dock too; each
// falls back to Tailwind's value, which is what Flux draws without a theme. The font is the kit's Inter when the page
// has it (a shadow root uses the document's faces, it cannot declare its own), else the system's sans.
const ZINC = (step: number, value: string) => `var(--color-zinc-${step},${value})`;
const Z200 = ZINC(200, "oklch(.92 .004 286.32)");
const Z400 = ZINC(400, "oklch(.705 .015 286.067)");
const Z500 = ZINC(500, "oklch(.552 .016 285.938)");
const Z700 = ZINC(700, "oklch(.37 .013 285.805)");
const Z800 = ZINC(800, "oklch(.274 .006 286.033)");

// Light, then what dark changes: Flux's `dark:` side of the same classes.
const LIGHT =
    "color-scheme:light;" +
    `--fg:${Z800};--muted:${Z500};--line:${Z200};--surface:#fff;` +
    `--accent:var(--color-accent,${Z800});--accent-fg:var(--color-accent-foreground,#fff);` +
    "--accent-line:rgb(0 0 0/.1);--wash:color-mix(in oklab," + Z800 + " 5%,transparent);" +
    "--wash-strong:color-mix(in oklab," + Z800 + " 10%,transparent);" +
    "--badge:var(--color-red-500,oklch(.637 .237 25.331));";
const DARK =
    "color-scheme:dark;" +
    `--fg:#fff;--muted:${Z400};--line:${Z700};--surface:${Z800};` +
    `--accent:var(--color-accent,#fff);--accent-fg:var(--color-accent-foreground,${Z800});` +
    "--accent-line:transparent;--wash:rgb(255 255 255/.1);--wash-strong:rgb(255 255 255/.15);" +
    "--badge:var(--color-red-600,oklch(.577 .245 27.325));";
// Tailwind's shadow-lg, which Flux gives what floats above the page (a modal's panel, a dropdown).
const FLOAT = "0 10px 15px -3px rgb(0 0 0/.1),0 4px 6px -4px rgb(0 0 0/.1)";
const FONT = 'Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif';

const CSS =
    ":host{all:initial;" + LIGHT + "}" +
    // Dark when the page says so (scheme.ts), or when it says nothing and the system is dark.
    ":host([data-scheme=dark]){" + DARK + "}" +
    "@media (prefers-color-scheme:dark){:host(:not([data-scheme=light])){" + DARK + "}}" +
    // Native-feel defaults the kit's sheet would give, said here because the sheet may be absent: no text selection on
    // the chrome, no double-tap zoom or grey tap flash on a button, the hand on what presses and the arrow on what cannot.
    ".pill,.bar{-webkit-user-select:none;user-select:none}" +
    "button{cursor:pointer;touch-action:manipulation;-webkit-tap-highlight-color:transparent}" +
    "button:disabled,button[aria-busy=true]{cursor:default}" +
    // The pill: Flux's primary button at size sm, lifted off the page by the shadow Flux gives what floats.
    ".pill{position:fixed;left:12px;bottom:12px;z-index:2147483647;box-sizing:border-box;display:inline-flex;" +
    "align-items:center;justify-content:center;gap:8px;height:32px;margin:0;padding:0 12px;border-radius:6px;" +
    "border:1px solid var(--accent-line);background:var(--accent);color:var(--accent-fg);white-space:nowrap;" +
    "font:500 14px/20px " + FONT + ";box-shadow:inset 0 1px rgb(255 255 255/.2)," + FLOAT + "}" +
    ".pill:hover{background:color-mix(in oklab,var(--accent),transparent 10%)}" +
    // The unseen-error count: Flux's solid red badge at size sm.
    ".dot{display:inline-flex;align-items:center;padding:4px 8px;border-radius:6px;background:var(--badge);" +
    "color:#fff;font:500 12px/16px " + FONT + ";white-space:nowrap}" +
    ".dot[hidden]{display:none}" +
    // The drawer: the edge of Flux's flyout — its surface, its line on the side that meets the page, and a lift.
    ".drawer{position:fixed;z-index:2147483647;display:flex;flex-direction:column;box-sizing:border-box;" +
    "background:var(--surface);color:var(--fg);font:400 14px/20px " + FONT + ";box-shadow:" + FLOAT + "}" +
    ".drawer[hidden]{display:none}" +
    ".drawer[data-side=bottom]{left:0;right:0;bottom:0;height:40vh;border-top:1px solid var(--line)}" +
    ".drawer[data-side=right]{top:0;right:0;bottom:0;width:min(440px,92vw);border-left:1px solid var(--line)}" +
    ".bar{display:flex;align-items:center;gap:4px;padding:4px 4px 4px 12px;border-bottom:1px solid var(--line)}" +
    // Flux's heading at its default size.
    ".title{margin-right:auto;font-weight:500;color:var(--fg)}" +
    // Flux's subtle button at size xs; the side the drawer is docked to is drawn as its filled button, and Close as ghost.
    ".btn{box-sizing:border-box;display:inline-flex;align-items:center;justify-content:center;height:24px;margin:0;" +
    "padding:0 8px;border:0;border-radius:6px;background:transparent;color:var(--muted);" +
    "font:500 12px/16px " + FONT + ";white-space:nowrap}" +
    ".btn:hover{background:var(--wash);color:var(--fg)}" +
    ".btn[aria-pressed=true]{background:var(--wash);color:var(--fg)}" +
    ".btn[aria-pressed=true]:hover{background:var(--wash-strong)}" +
    ".btn.close{color:var(--fg)}" +
    ".frame{flex:1;border:0;width:100%;background:#fff}";

function readSide(): Dock {
    try {
        return localStorage.getItem(DOCK_STORAGE_KEY) === "right" ? "right" : "bottom";
    } catch {
        // Storage blocked — a sandboxed frame, a privacy mode. The choice then lasts this page.
        return "bottom";
    }
}

function writeSide(side: Dock): void {
    try {
        localStorage.setItem(DOCK_STORAGE_KEY, side);
    } catch {
        // As above.
    }
}

/** Ctrl+Shift+D, or Cmd+Shift+D on a Mac. By physical key, so a keyboard layout cannot move it. */
export function isToggleShortcut(e: KeyboardEvent): boolean {
    return e.shiftKey && (e.ctrlKey || e.metaKey) && !e.altKey && e.code === "KeyD";
}

function button(className: string, text: string, label?: string): HTMLButtonElement {
    const b = document.createElement("button");
    b.type = "button";
    b.className = className;
    b.textContent = text;
    if (label) b.setAttribute("aria-label", label);
    return b;
}

export function installDock(options: DockOptions): DockHandle {
    const element = document.createElement("rask-devtools");
    element.setAttribute("data-rask-managed", "");
    const shadow = element.attachShadow({mode: "open"});

    const style = document.createElement("style");
    style.textContent = CSS;

    const pill = button("pill", "Rask", "Rask DevTools");
    const dot = document.createElement("span");
    dot.className = "dot";
    dot.hidden = true;
    dot.setAttribute("aria-hidden", "true");
    pill.appendChild(dot);
    let alertCount = 0;
    const showAlert = () => {
        dot.hidden = alertCount === 0 || !drawer.hidden;
        dot.textContent = alertCount > 99 ? "99+" : String(alertCount);
        pill.setAttribute("aria-label", alertCount === 0
            ? "Rask DevTools"
            : "Rask DevTools, " + alertCount + (alertCount === 1 ? " new error" : " new errors"));
    };
    pill.title = "Rask DevTools (Ctrl+Shift+D)";
    pill.setAttribute("aria-expanded", "false");
    pill.setAttribute("aria-controls", "drawer");

    const drawer = document.createElement("section");
    drawer.id = "drawer";
    drawer.className = "drawer";
    drawer.hidden = true;
    drawer.setAttribute("aria-label", "Rask DevTools");

    const bar = document.createElement("div");
    bar.className = "bar";
    const title = document.createElement("span");
    title.className = "title";
    title.textContent = "Rask DevTools";
    const toBottom = button("btn", "Bottom", "Dock to the bottom");
    const toRight = button("btn", "Right", "Dock to the right");
    const close = button("btn close", "Close", "Close Rask DevTools");
    bar.appendChild(title);
    bar.appendChild(toBottom);
    bar.appendChild(toRight);
    bar.appendChild(close);
    drawer.appendChild(bar);

    let current = readSide();
    let frame: HTMLIFrameElement | null = null;

    const applySide = () => {
        drawer.setAttribute("data-side", current);
        toBottom.setAttribute("aria-pressed", String(current === "bottom"));
        toRight.setAttribute("aria-pressed", String(current === "right"));
    };

    // The panel frame, created once: on first open, or earlier when the page needs the panel running with the drawer shut.
    const ensureFrame = () => {
        if (!frame && (options.panelUrl || options.frameDocument)) {
            frame = document.createElement("iframe");
            frame.className = "frame";
            frame.title = "Rask DevTools panel";
            if (options.panelUrl) {
                frame.src = options.panelUrl;
            } else {
                frame.srcdoc = options.frameDocument!;
            }
            drawer.appendChild(frame);
            // Attached first: a frame's window exists only once it is in the document.
            options.onFrame?.(frame);
        }
    };

    const open = () => {
        ensureFrame();
        drawer.hidden = false;
        pill.setAttribute("aria-expanded", "true");
        showAlert();
    };

    const shut = () => {
        drawer.hidden = true;
        pill.setAttribute("aria-expanded", "false");
        showAlert();
        pill.focus();
        options.onClose?.();
    };

    const handle: DockHandle = {
        element,
        shadow,
        isOpen: () => !drawer.hidden,
        toggle: () => (drawer.hidden ? open() : shut()),
        preload: ensureFrame,
        setAlert: (count: number) => {
            alertCount = Math.max(0, Math.floor(count));
            showAlert();
        },
        side: () => current,
        setSide: (side: Dock) => {
            current = side;
            writeSide(side);
            applySide();
        },
    };

    pill.addEventListener("click", () => handle.toggle());
    close.addEventListener("click", shut);
    toBottom.addEventListener("click", () => handle.setSide("bottom"));
    toRight.addEventListener("click", () => handle.setSide("right"));

    // Capture on window runs before every document listener, the runtime's key forwarding included, so the shortcut
    // opens the tools and never reaches the app as a keystroke.
    window.addEventListener("keydown", (e: KeyboardEvent) => {
        if (!isToggleShortcut(e)) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        handle.toggle();
    }, true);

    applySide();
    followPageScheme(element);
    shadow.appendChild(style);
    shadow.appendChild(pill);
    shadow.appendChild(drawer);
    document.documentElement.appendChild(element);
    return handle;
}
