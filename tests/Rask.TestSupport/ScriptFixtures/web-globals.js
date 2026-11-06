// The web platform globals a fixture and the module it drives reach for, which a bare ECMAScript engine has none
// of: timers, the console, events, encoders, streams, fetch's Response and Headers, an abort signal, base64.
//
// Each is the smallest faithful version of its standard — what the shipped modules and their fixtures call, with
// the standard's semantics for that much. The DOM itself is NOT here: every fixture stubs exactly the document it
// needs, which is the point of a fixture. Installed by FixtureRun before the bundle is imported; __raskFixture is
// the host it talks to (timers on a virtual clock, report, the console sink).
(function (host) {
    "use strict";

    const define = (name, value) =>
        Object.defineProperty(globalThis, name, {value, writable: true, configurable: true, enumerable: false});

    // --- console ----------------------------------------------------------------------------------------------------

    const show = (value) => {
        if (typeof value === "string") return value;
        if (value instanceof Error) return value.stack || String(value);
        try {
            const json = JSON.stringify(value);
            return json === undefined ? String(value) : json;
        } catch {
            return String(value);
        }
    };
    const printer = (level) => (...args) => host.print(level, args.map(show).join(" "));
    define("console", {
        log: printer("log"), info: printer("info"), debug: printer("debug"),
        warn: printer("warn"), error: printer("error"), trace: printer("trace"),
    });

    // --- timers, on the host's virtual clock --------------------------------------------------------------------------

    const timer = (repeat) => (callback, delay, ...args) => {
        if (typeof callback !== "function") throw new TypeError("The callback must be a function.");
        return host.setTimer(() => callback(...args), Number(delay), repeat);
    };
    define("setTimeout", timer(false));
    define("setInterval", timer(true));
    define("clearTimeout", (id) => host.clearTimer(id));
    define("clearInterval", (id) => host.clearTimer(id));
    define("queueMicrotask", (callback) => {
        Promise.resolve().then(() => {
            try {
                callback();
            } catch (e) {
                host.uncaught(e);
            }
        });
    });

    const timeOrigin = Date.now();
    define("performance", {
        timeOrigin,
        now: () => host.now(),
        mark() {},
        measure() {},
        getEntriesByName: () => [],
        getEntriesByType: () => [],
    });

    // --- errors -------------------------------------------------------------------------------------------------------

    // A stack led by its "Name: message" line, the shape V8 (Chromium, Node) gives and the shipped code was written
    // against: the devtools' error list reads its first line. The engine's own is the frames alone.
    const frames = Object.getOwnPropertyDescriptor(Error.prototype, "stack");
    if (frames?.get) {
        Object.defineProperty(Error.prototype, "stack", {
            configurable: true,
            get() {
                const below = frames.get.call(this);
                const head = this.message ? `${this.name}: ${this.message}` : String(this.name);
                return below ? head + "\n" + below : head;
            },
            set(value) {
                Object.defineProperty(this, "stack", {value, writable: true, configurable: true});
            },
        });
    }

    class DOMException extends Error {
        constructor(message = "", name = "Error") {
            super(message);
            Object.defineProperty(this, "name", {value: name, configurable: true, writable: true});
        }
    }
    define("DOMException", DOMException);

    // --- events -------------------------------------------------------------------------------------------------------

    class Event {
        #stopped = false;
        #immediate = false;

        constructor(type, init = {}) {
            if (arguments.length === 0) throw new TypeError("The event type is required.");
            this.type = String(type);
            this.bubbles = !!init.bubbles;
            this.cancelable = !!init.cancelable;
            this.composed = !!init.composed;
            this.defaultPrevented = false;
            this.isTrusted = false;
            this.target = null;
            this.currentTarget = null;
            this.eventPhase = 0;
            this.timeStamp = host.now();
        }

        preventDefault() {
            if (this.cancelable) this.defaultPrevented = true;
        }

        stopPropagation() { this.#stopped = true; }

        stopImmediatePropagation() {
            this.#stopped = true;
            this.#immediate = true;
        }

        get cancelBubble() { return this.#stopped; }

        get returnValue() { return !this.defaultPrevented; }

        /** For EventTarget.dispatchEvent below: whether the listener loop must stop. */
        static immediateStopped(event) { return event.#immediate; }
    }
    define("Event", Event);

    class CustomEvent extends Event {
        constructor(type, init = {}) {
            super(type, init);
            this.detail = init.detail ?? null;
        }
    }
    define("CustomEvent", CustomEvent);

    const listenersOf = new WeakMap();

    class EventTarget {
        addEventListener(type, listener, options) {
            if (listener == null) return;
            const capture = typeof options === "boolean" ? options : !!options?.capture;
            const once = typeof options === "object" && !!options?.once;
            const signal = typeof options === "object" ? options?.signal : undefined;
            if (signal?.aborted) return;
            let all = listenersOf.get(this);
            if (!all) listenersOf.set(this, all = new Map());
            let list = all.get(type);
            if (!list) all.set(type, list = []);
            if (list.some((l) => l.listener === listener && l.capture === capture)) return;
            const entry = {listener, capture, once, removed: false};
            list.push(entry);
            signal?.addEventListener("abort", () => this.removeEventListener(type, listener, {capture}));
        }

        removeEventListener(type, listener, options) {
            const capture = typeof options === "boolean" ? options : !!options?.capture;
            const list = listenersOf.get(this)?.get(type);
            if (!list) return;
            const index = list.findIndex((l) => l.listener === listener && l.capture === capture);
            if (index >= 0) {
                list[index].removed = true;
                list.splice(index, 1);
            }
        }

        dispatchEvent(event) {
            if (!(event instanceof Event)) throw new TypeError("dispatchEvent takes an Event.");
            event.target = this;
            event.currentTarget = this;
            event.eventPhase = 2;
            const handler = this["on" + event.type];
            const list = [...(listenersOf.get(this)?.get(event.type) ?? [])];
            if (typeof handler === "function") list.push({listener: handler, once: false, removed: false});
            for (const entry of list) {
                if (entry.removed) continue;
                if (entry.once) this.removeEventListener(event.type, entry.listener, {capture: entry.capture});
                try {
                    const listener = entry.listener;
                    if (typeof listener === "function") listener.call(this, event);
                    else listener.handleEvent(event);
                } catch (e) {
                    host.uncaught(e);
                }
                if (Event.immediateStopped(event)) break;
            }
            event.currentTarget = null;
            event.eventPhase = 0;
            return !event.defaultPrevented;
        }
    }
    define("EventTarget", EventTarget);

    // --- abort ----------------------------------------------------------------------------------------------------------

    class AbortSignal extends EventTarget {
        constructor() {
            super();
            this.aborted = false;
            this.reason = undefined;
            this.onabort = null;
        }

        throwIfAborted() {
            if (this.aborted) throw this.reason;
        }

        static abort(reason) {
            const controller = new AbortController();
            controller.abort(reason);
            return controller.signal;
        }

        static timeout(ms) {
            const controller = new AbortController();
            setTimeout(() => controller.abort(new DOMException("The operation timed out.", "TimeoutError")), ms);
            return controller.signal;
        }

        static any(signals) {
            const controller = new AbortController();
            for (const signal of signals) {
                if (signal.aborted) {
                    controller.abort(signal.reason);
                    break;
                }
                signal.addEventListener("abort", () => controller.abort(signal.reason), {once: true});
            }
            return controller.signal;
        }
    }
    define("AbortSignal", AbortSignal);

    class AbortController {
        constructor() {
            this.signal = new AbortSignal();
        }

        abort(reason = new DOMException("This operation was aborted", "AbortError")) {
            const signal = this.signal;
            if (signal.aborted) return;
            signal.aborted = true;
            signal.reason = reason;
            signal.dispatchEvent(new Event("abort"));
        }
    }
    define("AbortController", AbortController);

    // --- text encoding (UTF-8) ------------------------------------------------------------------------------------------

    class TextEncoder {
        get encoding() { return "utf-8"; }

        encode(input = "") {
            const text = String(input);
            const bytes = [];
            for (let i = 0; i < text.length; i++) {
                let code = text.charCodeAt(i);
                if (code >= 0xd800 && code <= 0xdbff && i + 1 < text.length) {
                    const low = text.charCodeAt(i + 1);
                    if (low >= 0xdc00 && low <= 0xdfff) {
                        code = 0x10000 + ((code - 0xd800) << 10) + (low - 0xdc00);
                        i++;
                    } else {
                        code = 0xfffd;
                    }
                } else if (code >= 0xd800 && code <= 0xdfff) {
                    code = 0xfffd;
                }
                if (code < 0x80) bytes.push(code);
                else if (code < 0x800) bytes.push(0xc0 | (code >> 6), 0x80 | (code & 63));
                else if (code < 0x10000) bytes.push(0xe0 | (code >> 12), 0x80 | ((code >> 6) & 63), 0x80 | (code & 63));
                else bytes.push(0xf0 | (code >> 18), 0x80 | ((code >> 12) & 63), 0x80 | ((code >> 6) & 63), 0x80 | (code & 63));
            }
            return new Uint8Array(bytes);
        }
    }
    define("TextEncoder", TextEncoder);

    const bytesOf = (input) => {
        if (input === undefined) return new Uint8Array(0);
        if (input instanceof ArrayBuffer) return new Uint8Array(input);
        if (ArrayBuffer.isView(input)) return new Uint8Array(input.buffer, input.byteOffset, input.byteLength);
        throw new TypeError("decode takes an ArrayBuffer or a view of one.");
    };

    class TextDecoder {
        #pending = [];
        #fatal;

        constructor(label = "utf-8", options = {}) {
            if (!/^utf-?8$/i.test(String(label))) throw new RangeError(`Only UTF-8 is decoded here, not '${label}'.`);
            this.#fatal = !!options.fatal;
        }

        get encoding() { return "utf-8"; }

        decode(input, options = {}) {
            const bytes = [...this.#pending, ...bytesOf(input)];
            this.#pending = [];
            let text = "";
            let i = 0;
            const bad = () => {
                if (this.#fatal) throw new TypeError("The encoded data was not valid UTF-8.");
                text += "�";
            };
            while (i < bytes.length) {
                const lead = bytes[i];
                const length = lead < 0x80 ? 1 : lead >= 0xf0 && lead < 0xf5 ? 4 : lead >= 0xe0 ? 3 : lead >= 0xc2 && lead < 0xe0 ? 2 : 0;
                if (length === 0) {
                    bad();
                    i++;
                    continue;
                }
                if (i + length > bytes.length) {
                    if (options.stream) {
                        this.#pending = bytes.slice(i);
                        return text;
                    }
                    bad();
                    break;
                }
                let code = length === 1 ? lead : lead & (0xff >> (length + 1));
                let valid = true;
                for (let k = 1; k < length; k++) {
                    const next = bytes[i + k];
                    if ((next & 0xc0) !== 0x80) {
                        valid = false;
                        break;
                    }
                    code = (code << 6) | (next & 63);
                }
                if (!valid) {
                    bad();
                    i++;
                    continue;
                }
                text += String.fromCodePoint(code);
                i += length;
            }
            return text;
        }
    }
    define("TextDecoder", TextDecoder);

    // --- base64 ---------------------------------------------------------------------------------------------------------

    const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    define("btoa", (input) => {
        const text = String(input);
        let out = "";
        for (let i = 0; i < text.length; i += 3) {
            const codes = [text.charCodeAt(i), text.charCodeAt(i + 1), text.charCodeAt(i + 2)];
            if (codes.some((c) => c > 0xff)) {
                throw new DOMException("The string contains characters outside of the Latin1 range.", "InvalidCharacterError");
            }
            const [a, b, c] = codes;
            out += alphabet[a >> 2] + alphabet[((a & 3) << 4) | (b >> 4 || 0)]
                + (Number.isNaN(b) ? "=" : alphabet[((b & 15) << 2) | (c >> 6 || 0)])
                + (Number.isNaN(c) ? "=" : alphabet[c & 63]);
        }
        return out;
    });
    define("atob", (input) => {
        const clean = String(input).replace(/[\t\n\f\r ]/g, "").replace(/={1,2}$/, "");
        if (/[^A-Za-z0-9+/]/.test(clean) || clean.length % 4 === 1) {
            throw new DOMException("The string to be decoded is not correctly encoded.", "InvalidCharacterError");
        }
        let out = "";
        let bits = 0;
        let value = 0;
        for (const ch of clean) {
            value = (value << 6) | alphabet.indexOf(ch);
            bits += 6;
            if (bits >= 8) {
                bits -= 8;
                out += String.fromCharCode((value >> bits) & 0xff);
            }
        }
        return out;
    });

    // --- crypto ---------------------------------------------------------------------------------------------------------

    define("crypto", {
        randomUUID: () => host.randomUuid(),
        getRandomValues(array) {
            const bytes = new Uint8Array(array.buffer, array.byteOffset, array.byteLength);
            for (let i = 0; i < bytes.length; i++) bytes[i] = host.randomByte();
            return array;
        },
    });

    // --- structured clone -----------------------------------------------------------------------------------------------

    define("structuredClone", (value) => {
        const seen = new Map();
        const clone = (v) => {
            if (typeof v === "function" || typeof v === "symbol") {
                throw new DOMException("The value could not be cloned.", "DataCloneError");
            }
            if (v === null || typeof v !== "object") return v;
            if (seen.has(v)) return seen.get(v);
            let out;
            if (v instanceof Date) out = new Date(v.getTime());
            else if (v instanceof RegExp) out = new RegExp(v.source, v.flags);
            else if (v instanceof ArrayBuffer) out = v.slice(0);
            else if (ArrayBuffer.isView(v)) out = new v.constructor(v);
            else if (v instanceof Map) {
                out = new Map();
                seen.set(v, out);
                for (const [k, item] of v) out.set(clone(k), clone(item));
                return out;
            } else if (v instanceof Set) {
                out = new Set();
                seen.set(v, out);
                for (const item of v) out.add(clone(item));
                return out;
            } else if (Array.isArray(v)) {
                out = [];
                seen.set(v, out);
                for (let i = 0; i < v.length; i++) out[i] = clone(v[i]);
                return out;
            } else {
                out = {};
                seen.set(v, out);
                for (const key of Object.keys(v)) out[key] = clone(v[key]);
                return out;
            }
            seen.set(v, out);
            return out;
        };
        return clone(value);
    });

    // --- streams --------------------------------------------------------------------------------------------------------

    class ReadableStream {
        #queue = [];
        #waiting = [];
        #state = "readable";
        #error;
        #source;
        #locked = false;

        constructor(source = {}) {
            this.#source = source;
            const controller = {
                enqueue: (chunk) => {
                    if (this.#state !== "readable") throw new TypeError("The stream is closed.");
                    const reader = this.#waiting.shift();
                    if (reader) reader.resolve({value: chunk, done: false});
                    else this.#queue.push(chunk);
                },
                close: () => {
                    if (this.#state !== "readable") return;
                    this.#state = "closed";
                    for (const reader of this.#waiting.splice(0)) reader.resolve({value: undefined, done: true});
                },
                error: (e) => {
                    if (this.#state !== "readable") return;
                    this.#state = "errored";
                    this.#error = e;
                    for (const reader of this.#waiting.splice(0)) reader.reject(e);
                },
                get desiredSize() { return 1; },
            };
            this.controller = controller;
            Object.defineProperty(this, "controller", {enumerable: false});
            try {
                const started = source.start?.(controller);
                if (started && typeof started.then === "function") started.then(undefined, controller.error);
            } catch (e) {
                controller.error(e);
            }
        }

        get locked() { return this.#locked; }

        read() {
            if (this.#queue.length > 0) return Promise.resolve({value: this.#queue.shift(), done: false});
            if (this.#state === "closed") return Promise.resolve({value: undefined, done: true});
            if (this.#state === "errored") return Promise.reject(this.#error);
            const pending = new Promise((resolve, reject) => this.#waiting.push({resolve, reject}));
            try {
                this.#source.pull?.(this.controller);
            } catch (e) {
                this.controller.error(e);
            }
            return pending;
        }

        cancel(reason) {
            this.#queue = [];
            if (this.#state === "readable") {
                this.#state = "closed";
                for (const reader of this.#waiting.splice(0)) reader.resolve({value: undefined, done: true});
                try {
                    this.#source.cancel?.(reason);
                } catch {
                    // A source that fails to cancel is still cancelled, as far as its reader is concerned.
                }
            }
            return Promise.resolve();
        }

        getReader() {
            if (this.#locked) throw new TypeError("The stream is already locked to a reader.");
            this.#locked = true;
            const stream = this;
            let released = false;
            return {
                read: () => (released ? Promise.reject(new TypeError("The reader was released.")) : stream.read()),
                cancel: (reason) => stream.cancel(reason),
                releaseLock: () => {
                    released = true;
                    stream.#locked = false;
                },
                get closed() {
                    return new Promise((resolve) => {
                        if (stream.#state !== "readable") resolve(undefined);
                    });
                },
            };
        }

        async* [Symbol.asyncIterator]() {
            const reader = this.getReader();
            try {
                for (;;) {
                    const {value, done} = await reader.read();
                    if (done) return;
                    yield value;
                }
            } finally {
                reader.releaseLock();
            }
        }
    }
    define("ReadableStream", ReadableStream);

    // --- fetch's Headers and Response -------------------------------------------------------------------------------------

    class Headers {
        #map = new Map();

        constructor(init) {
            if (init instanceof Headers) for (const [k, v] of init) this.append(k, v);
            else if (Array.isArray(init)) for (const [k, v] of init) this.append(k, v);
            else if (init && typeof init === "object") for (const k of Object.keys(init)) this.append(k, init[k]);
        }

        append(name, value) {
            const key = String(name).toLowerCase();
            const existing = this.#map.get(key);
            this.#map.set(key, existing === undefined ? String(value) : existing + ", " + String(value));
        }

        set(name, value) { this.#map.set(String(name).toLowerCase(), String(value)); }

        get(name) { return this.#map.get(String(name).toLowerCase()) ?? null; }

        has(name) { return this.#map.has(String(name).toLowerCase()); }

        delete(name) { this.#map.delete(String(name).toLowerCase()); }

        forEach(callback, thisArg) {
            for (const [k, v] of this) callback.call(thisArg, v, k, this);
        }

        * entries() { yield* [...this.#map.entries()].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)); }

        * keys() { for (const [k] of this.entries()) yield k; }

        * values() { for (const [, v] of this.entries()) yield v; }

        [Symbol.iterator]() { return this.entries(); }
    }
    define("Headers", Headers);

    // --- URL ------------------------------------------------------------------------------------------------------------

    // application/x-www-form-urlencoded, as URLSearchParams serializes and parses it.
    const formEncode = (text) =>
        encodeURIComponent(text).replace(/[!'()~]/g, (c) => "%" + c.charCodeAt(0).toString(16).toUpperCase())
            .replace(/%20/g, "+");
    const formDecode = (text) => {
        try {
            return decodeURIComponent(text.replace(/\+/g, " "));
        } catch {
            return text.replace(/\+/g, " ");
        }
    };

    class URLSearchParams {
        #pairs = [];
        #changed;

        constructor(init = "", changed = undefined) {
            this.#changed = changed;
            if (init instanceof URLSearchParams) this.#pairs = [...init.#pairs];
            else if (typeof init === "string") this.#parse(init);
            else if (init != null && typeof init[Symbol.iterator] === "function") {
                for (const [k, v] of init) this.#pairs.push([String(k), String(v)]);
            } else if (init != null && typeof init === "object") {
                for (const k of Object.keys(init)) this.#pairs.push([k, String(init[k])]);
            }
        }

        #parse(text) {
            this.#pairs = [];
            for (const part of text.replace(/^\?/, "").split("&")) {
                if (part === "") continue;
                const eq = part.indexOf("=");
                this.#pairs.push(eq < 0 ? [formDecode(part), ""] : [formDecode(part.slice(0, eq)), formDecode(part.slice(eq + 1))]);
            }
        }

        /** For URL below: its query changed, so these follow it. */
        static reparse(params, text) { params.#parse(text); }

        #update() { this.#changed?.(this.toString()); }

        get size() { return this.#pairs.length; }

        append(name, value) {
            this.#pairs.push([String(name), String(value)]);
            this.#update();
        }

        delete(name, value) {
            this.#pairs = this.#pairs.filter(([k, v]) => k !== String(name) || (value !== undefined && v !== String(value)));
            this.#update();
        }

        get(name) { return this.#pairs.find(([k]) => k === String(name))?.[1] ?? null; }

        getAll(name) { return this.#pairs.filter(([k]) => k === String(name)).map(([, v]) => v); }

        has(name, value) {
            return this.#pairs.some(([k, v]) => k === String(name) && (value === undefined || v === String(value)));
        }

        set(name, value) {
            const key = String(name);
            const index = this.#pairs.findIndex(([k]) => k === key);
            if (index < 0) this.#pairs.push([key, String(value)]);
            else this.#pairs = this.#pairs.filter(([k], i) => k !== key || i === index).map((p, i) => (i === index ? [key, String(value)] : p));
            this.#update();
        }

        sort() {
            this.#pairs.sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
            this.#update();
        }

        forEach(callback, thisArg) {
            for (const [k, v] of this.#pairs) callback.call(thisArg, v, k, this);
        }

        * entries() { yield* this.#pairs.map((p) => [...p]); }

        * keys() { for (const [k] of this.#pairs) yield k; }

        * values() { for (const [, v] of this.#pairs) yield v; }

        [Symbol.iterator]() { return this.entries(); }

        toString() { return this.#pairs.map(([k, v]) => formEncode(k) + "=" + formEncode(v)).join("&"); }
    }
    define("URLSearchParams", URLSearchParams);

    const defaultPorts = {"http:": "80", "https:": "443", "ws:": "80", "wss:": "443", "ftp:": "21", "file:": ""};
    const isSpecial = (protocol) => Object.hasOwn(defaultPorts, protocol);
    // What a browser percent-encodes when it parses a path, a query or a fragment: controls, space, the quote and
    // angle brackets, the backtick, and everything past ASCII. A % already there is left alone.
    const escapeIn = (text, extra) =>
        text.replace(new RegExp(`[\\u0000-\\u0020"<>\`\\u007f-\\uffff${extra}]`, "gu"),
            (c) => [...new TextEncoder().encode(c)].map((b) => "%" + b.toString(16).toUpperCase().padStart(2, "0")).join(""));

    const removeDotSegments = (path) => {
        const out = [];
        const segments = path.split("/");
        for (let i = 0; i < segments.length; i++) {
            const segment = segments[i];
            const last = i === segments.length - 1;
            if (segment === "." || segment.toLowerCase() === "%2e") {
                if (last) out.push("");
            } else if (segment === ".." || /^(\.|%2e){2}$/i.test(segment)) {
                if (out.length > 1) out.pop();
                if (last) out.push("");
            } else {
                out.push(segment);
            }
        }
        return out.join("/");
    };

    const parseAbsolute = (input) => {
        const scheme = /^([a-zA-Z][a-zA-Z0-9+.-]*):(.*)$/s.exec(input);
        if (!scheme) return null;
        const protocol = scheme[1].toLowerCase() + ":";
        let rest = scheme[2];
        const parts = {protocol, username: "", password: "", hostname: "", port: "", pathname: "", search: "", hash: "", opaque: false};
        const hashAt = rest.indexOf("#");
        if (hashAt >= 0) {
            parts.hash = rest.slice(hashAt);
            rest = rest.slice(0, hashAt);
        }
        const queryAt = rest.indexOf("?");
        if (queryAt >= 0) {
            parts.search = rest.slice(queryAt);
            rest = rest.slice(0, queryAt);
        }
        if (isSpecial(protocol)) rest = rest.replace(/\\/g, "/");
        if (rest.startsWith("//") || (isSpecial(protocol) && protocol !== "file:")) {
            rest = rest.replace(/^\/*/, "");
            const slash = rest.indexOf("/");
            let authority = slash < 0 ? rest : rest.slice(0, slash);
            rest = slash < 0 ? "" : rest.slice(slash);
            const at = authority.lastIndexOf("@");
            if (at >= 0) {
                const credentials = authority.slice(0, at);
                authority = authority.slice(at + 1);
                const colon = credentials.indexOf(":");
                parts.username = colon < 0 ? credentials : credentials.slice(0, colon);
                parts.password = colon < 0 ? "" : credentials.slice(colon + 1);
            }
            const port = /:(\d*)$/.exec(authority);
            if (port) {
                authority = authority.slice(0, port.index);
                parts.port = port[1] === "" || port[1] === defaultPorts[protocol] ? "" : String(Number(port[1]));
            }
            parts.hostname = isSpecial(protocol) ? authority.toLowerCase() : authority;
            if (isSpecial(protocol) && protocol !== "file:" && parts.hostname === "") return undefined;
            parts.pathname = rest === "" && isSpecial(protocol) ? "/" : rest;
        } else if (rest.startsWith("/") || protocol === "file:") {
            parts.pathname = rest.startsWith("/") ? rest : "/" + rest;
        } else {
            parts.pathname = rest;
            parts.opaque = true;
        }
        if (!parts.opaque) parts.pathname = escapeIn(removeDotSegments(parts.pathname), "?#");
        return parts;
    };

    const parse = (input, base) => {
        const text = String(input).trim();
        const absolute = parseAbsolute(text);
        if (absolute && !(base && !absolute.opaque && isSpecial(absolute.protocol) && absolute.protocol === base.protocol
            && !/^[a-zA-Z][a-zA-Z0-9+.-]*:[\\/]/.test(text))) {
            return absolute;
        }
        if (!base || base.opaque) return undefined;
        const out = {...base, hash: ""};
        let rest = isSpecial(base.protocol) ? text.replace(/\\/g, "/") : text;
        if (absolute) rest = rest.slice(absolute.protocol.length);
        if (rest.startsWith("//")) return parseAbsolute(base.protocol + rest);
        const hashAt = rest.indexOf("#");
        if (hashAt >= 0) {
            out.hash = rest.slice(hashAt);
            rest = rest.slice(0, hashAt);
        }
        if (rest === "") return out;
        const queryAt = rest.indexOf("?");
        const path = queryAt >= 0 ? rest.slice(0, queryAt) : rest;
        out.search = queryAt >= 0 ? rest.slice(queryAt) : (path === "" ? base.search : "");
        if (path !== "") {
            const merged = path.startsWith("/") ? path : base.pathname.slice(0, base.pathname.lastIndexOf("/") + 1) + path;
            out.pathname = escapeIn(removeDotSegments(merged), "?#");
        }
        return out;
    };

    class URL {
        #parts;
        #params;

        constructor(input, base) {
            const baseParts = base === undefined ? undefined : (base instanceof URL ? base.#parts : parse(String(base)));
            if (base !== undefined && !baseParts) throw new TypeError(`Invalid base URL: ${base}`);
            const parts = parse(input, baseParts);
            if (!parts) throw new TypeError(`Invalid URL: ${input}`);
            this.#parts = parts;
            this.#parts.search = this.#parts.search === "?" ? "" : escapeIn(this.#parts.search, "#'");
            this.#parts.hash = this.#parts.hash === "#" ? "" : escapeIn(this.#parts.hash, "");
            this.#params = new URLSearchParams(this.#parts.search, (query) => {
                this.#parts.search = query === "" ? "" : "?" + query;
            });
        }

        static canParse(input, base) {
            try {
                new URL(input, base);
                return true;
            } catch {
                return false;
            }
        }

        get protocol() { return this.#parts.protocol; }

        get username() { return this.#parts.username; }

        get password() { return this.#parts.password; }

        get hostname() { return this.#parts.hostname; }

        get port() { return this.#parts.port; }

        get host() { return this.#parts.hostname + (this.#parts.port ? ":" + this.#parts.port : ""); }

        get origin() {
            return isSpecial(this.protocol) && this.protocol !== "file:" ? this.protocol + "//" + this.host : "null";
        }

        get pathname() { return this.#parts.pathname; }

        set pathname(value) { this.#parts.pathname = escapeIn(removeDotSegments((String(value).startsWith("/") ? "" : "/") + value), "?#"); }

        get search() { return this.#parts.search; }

        set search(value) {
            const text = String(value).replace(/^\?/, "");
            this.#parts.search = text === "" ? "" : "?" + escapeIn(text, "#'");
            URLSearchParams.reparse(this.#params, this.#parts.search);
        }

        get searchParams() { return this.#params; }

        get hash() { return this.#parts.hash; }

        set hash(value) {
            const text = String(value).replace(/^#/, "");
            this.#parts.hash = text === "" ? "" : "#" + escapeIn(text, "");
        }

        get href() {
            const p = this.#parts;
            if (p.opaque) return p.protocol + p.pathname + p.search + p.hash;
            const credentials = p.username || p.password ? p.username + (p.password ? ":" + p.password : "") + "@" : "";
            const authority = isSpecial(p.protocol) || p.hostname !== "" ? "//" + credentials + this.host : "";
            return p.protocol + authority + p.pathname + p.search + p.hash;
        }

        toString() { return this.href; }

        toJSON() { return this.href; }
    }
    define("URL", URL);

    // --- navigator --------------------------------------------------------------------------------------------------------

    // Node's own, which is what the fixtures were written against: an identity and a language, and none of the
    // device APIs — a fixture that needs one stubs it.
    define("navigator", {userAgent: "Rask script fixture", language: "en-US", languages: ["en-US"], hardwareConcurrency: 1, platform: ""});

    const encoder = new TextEncoder();

    const streamOf = (body) => {
        if (body == null) return null;
        if (body instanceof ReadableStream) return body;
        const bytes = typeof body === "string" ? encoder.encode(body) : bytesOf(body);
        return new ReadableStream({
            start(controller) {
                controller.enqueue(bytes);
                controller.close();
            },
        });
    };

    class Response {
        #source;

        constructor(body = null, init = {}) {
            this.#source = body;
            this.status = init.status ?? 200;
            this.statusText = init.statusText ?? "";
            this.headers = new Headers(init.headers);
            if (typeof body === "string" && !this.headers.has("content-type")) {
                this.headers.set("content-type", "text/plain;charset=UTF-8");
            }
            this.type = "default";
            this.url = "";
            this.redirected = false;
            this.bodyUsed = false;
            this.body = streamOf(body);
        }

        get ok() { return this.status >= 200 && this.status <= 299; }

        async arrayBuffer() {
            if (this.bodyUsed) throw new TypeError("The body has already been read.");
            this.bodyUsed = true;
            if (!this.body) return new ArrayBuffer(0);
            const chunks = [];
            const reader = this.body.getReader();
            for (;;) {
                const {value, done} = await reader.read();
                if (done) break;
                chunks.push(...bytesOf(value));
            }
            return new Uint8Array(chunks).buffer;
        }

        async text() { return new TextDecoder().decode(await this.arrayBuffer()); }

        async json() { return JSON.parse(await this.text()); }

        clone() {
            if (this.bodyUsed) throw new TypeError("A read body cannot be cloned.");
            // A stream cannot be read twice without teeing it, which nothing here has needed yet: say so rather
            // than hand back a copy with no body.
            if (this.#source instanceof ReadableStream) throw new TypeError("A streamed body cannot be cloned here.");
            const copy = new Response(this.#source, this);
            copy.headers = new Headers(this.headers);
            return copy;
        }

        static json(data, init = {}) {
            const headers = new Headers(init.headers);
            if (!headers.has("content-type")) headers.set("content-type", "application/json");
            return new Response(JSON.stringify(data), {...init, headers});
        }

        static error() {
            const response = new Response(null, {status: 0});
            response.type = "error";
            return response;
        }
    }
    define("Response", Response);

    define("fetch", () => Promise.reject(new TypeError("fetch failed: a fixture has no network, so it stubs fetch itself.")));
})(__raskFixture);
