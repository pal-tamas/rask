// A phone field (data-rask-phone="<engine url>"): Ui.Phone.
//
// The field is Flux's: a number formatted as it is typed, a country that follows the number, a picker of every
// country. All of that is intl-tel-input — the library fluxui.dev runs — and the picker drawn around it, which
// is the kit's ENGINE, a file of its own (rask-ui-phone.js) that this hook fetches the first time a page carries
// a phone field, from the URL the attribute names. A page with none never fetches it.
//
// THE VALUE. The field holds ONE bound input (an <input type="hidden">), which the engine keeps equal to what the
// library reads off the field — E.164 once the number is possible, the text as typed until then — and announces
// the way every group the browser owns does (rask-bound.ts): typing, the picker and leaving the field send the
// server nothing, and the value goes with the next action, as Flux's does with wire:model. When the page writes
// the value itself, the field shows it.
//
// Before the engine has arrived the field is a plain tel input beside the server's drawing of the country, and
// what is typed into it then is formatted once the engine mounts.

import {announce, bind, boundOf} from "./rask-bound.js";
import {listen, own, page} from "./rask-owned.js";

/** What the engine module exports (Rask.Ui's Resources/phone/ui-phone.ts). */
interface Engine {
    mount(root: Element, kit: {announce: typeof announce; own: typeof own}): void;
    current(root: Element): string | null;
    adopt(root: Element, value: string): void;
    handle(e: Event): void;
}

const ROOT = "[data-rask-phone]";
const kit = {announce, own};
let engine: Engine | null = null;
let loading: Promise<Engine> | null = null;

function load(root: Element): Promise<Engine> {
    const src = root.getAttribute("data-rask-phone") || "";
    loading ??= (import(/* @vite-ignore */ src) as Promise<Engine>).then(function (module) {
        engine = module;
        return module;
    }, function (error) {
        loading = null;
        // Said here, because this is where an app's author looks: the field stays a plain one and goes on.
        console.error("Ui.Phone: its engine could not be loaded from " + src + ". The app's build writes it to "
            + "wwwroot/js/rask-ui-phone.js: check that the host serves its static files, and that the project "
            + "does not say RaskUiPhoneEngine=false. (" + error + ")");
        throw error;
    });
    return loading;
}

function start(root: Element): void {
    load(root).then(function (module) { module.mount(root, kit); }, function () { /* said in load */ });
}

// A field the page brought (or that was there when this arrived) mounts; a value the page wrote is shown.
bind(ROOT, function (root) {
    const value = engine ? engine.current(root) : null;
    if (value === null) {
        start(root);
        const bound = boundOf(root);
        return bound ? bound.getAttribute("value") || "" : "";
    }
    return value;
}, function (root, rendered) {
    if (engine) {
        engine.adopt(root, rendered);
    } else {
        start(root);
    }
});

if (page) {
    // Every field already on the page mounts now. The bound field's watch above adopts only a field that holds a
    // value when the hooks arrive late (rask-bound.ts), and an empty field is a field all the same.
    page.querySelectorAll(ROOT).forEach(start);

    // What the picker hears: opening and closing it, the keys of its button and of its search field, the
    // pointer over its rows and a press on one. The engine does the rest; nothing reaches the server.
    const heard = function (e: Event): void {
        const target = e.target;
        if (engine && target instanceof Element && target.closest(ROOT)) {
            engine.handle(e);
        }
    };
    listen("beforetoggle", heard, true);
    listen("keydown", heard, true);
    listen("input", heard, true);
    // A finger lights no row as it lands: measured on Flux UI's phone page (2026-10-27), a row held under a
    // finger is not lit, as a row of every other list is lit only by the mouse's events (rask-list.ts).
    listen("pointerover", function (e) {
        if ((e as PointerEvent).pointerType !== "touch") {
            heard(e);
        }
    }, true);
    listen("click", heard, true);
}
