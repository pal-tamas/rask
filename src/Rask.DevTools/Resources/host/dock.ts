// The devtools' place on the page: a corner pill that opens a drawer docked to the bottom or the right edge.
//
// Everything lives in an open shadow root on a <rask-devtools> element appended to <html>, after <body>:
//   * the app's stylesheets cannot reach inside it, whatever they select;
//   * data-rask-managed keeps the morph from removing it on a full frame;
//   * <body> keeps its place among <html>'s children, which the diff's positional paths walk through.
// DOM APIs only, never innerHTML, so a strict Content-Security-Policy changes nothing. Nothing covers the page until
// the drawer is opened, and the panel iframe is not created until then either.
//
// The pill sits bottom-LEFT: bottom-right is the hot-reload pill's, and the dev-error panel spans the bottom centre.

export type Dock = "bottom" | "right";

export const DOCK_STORAGE_KEY = "rask.devtools.dock";

export interface DockOptions {
    /** The panel page the drawer frames, or null while there is none to show. */
    readonly panelUrl: string | null;
}

export interface DockHandle {
    readonly element: HTMLElement;
    isOpen(): boolean;
    toggle(): void;
    side(): Dock;
    setSide(side: Dock): void;
}

const CSS =
    ":host{all:initial}" +
    ".pill{position:fixed;left:12px;bottom:12px;z-index:2147483647;padding:5px 12px;border-radius:999px;" +
    "border:1px solid rgba(255,255,255,.28);background:#1d1b26;color:#fff;cursor:pointer;" +
    "font:600 12px/1.4 system-ui,-apple-system,Segoe UI,sans-serif;box-shadow:0 4px 16px rgba(0,0,0,.3)}" +
    ".pill:focus-visible,.btn:focus-visible{outline:2px solid #8b7cf6;outline-offset:2px}" +
    ".drawer{position:fixed;z-index:2147483647;display:flex;flex-direction:column;background:#15141c;color:#ece9f5;" +
    "font:12px/1.4 system-ui,-apple-system,Segoe UI,sans-serif;box-shadow:0 0 24px rgba(0,0,0,.35)}" +
    ".drawer[hidden]{display:none}" +
    ".drawer[data-side=bottom]{left:0;right:0;bottom:0;height:40vh;border-top:1px solid #34313f}" +
    ".drawer[data-side=right]{top:0;right:0;bottom:0;width:min(440px,92vw);border-left:1px solid #34313f}" +
    ".bar{display:flex;align-items:center;gap:6px;padding:4px 8px;border-bottom:1px solid #34313f}" +
    ".title{font-weight:600;margin-right:auto}" +
    ".btn{border:1px solid #4a4658;background:transparent;color:inherit;border-radius:6px;padding:2px 8px;" +
    "font:inherit;cursor:pointer}" +
    ".btn[aria-pressed=true]{background:#34313f}" +
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
    const close = button("btn", "Close", "Close Rask DevTools");
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

    const open = () => {
        if (!frame && options.panelUrl) {
            frame = document.createElement("iframe");
            frame.className = "frame";
            frame.title = "Rask DevTools panel";
            frame.src = options.panelUrl;
            drawer.appendChild(frame);
        }
        drawer.hidden = false;
        pill.setAttribute("aria-expanded", "true");
    };

    const shut = () => {
        drawer.hidden = true;
        pill.setAttribute("aria-expanded", "false");
        pill.focus();
    };

    const handle: DockHandle = {
        element,
        isOpen: () => !drawer.hidden,
        toggle: () => (drawer.hidden ? open() : shut()),
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
    shadow.appendChild(style);
    shadow.appendChild(pill);
    shadow.appendChild(drawer);
    document.documentElement.appendChild(element);
    return handle;
}
