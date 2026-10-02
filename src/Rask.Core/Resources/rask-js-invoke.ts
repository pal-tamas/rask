// The browser half of IJSRuntime, the same on both hosts.
//
// Mirrors the Microsoft.JSInterop contract: .NET sends an "identifier" like "sessionStorage.getItem",
// it is resolved on window (or on a kept JS object), invoked with the JSON-decoded args, and the
// outcome goes back keyed by the task id .NET assigned. JSObjectReference returns get a stable handle
// id; DotNetObjectReference values flow back via a {__dotNetObject:<id>} placeholder so the .NET side
// can re-hydrate them.
//
// HOW the outcome goes back is the one thing the hosts do differently — a jsResult frame over the
// socket on the Server host, the EndInvokeJSResult JSExport on WASM — so it is the caller's `reply`.
//
// No DOM access at import: everything here runs per call.

/** Ships an invoke's outcome back to .NET, by whatever means the host has. */
export type JsInvokeReply = (success: boolean, result: unknown, error?: string) => void;

// The JS objects .NET holds an IJSObjectReference to, by handle id. One registry per document, shared
// by an invoke's JSObjectReference result and the DotNet shim's createJSObjectReference, so the two
// can never mint the same id.
const jsObjectRefs = new Map<number, unknown>();
let nextJsObjectRefId = 1;

/**
 * Holds `value` for .NET, which reads what this returns as an `IJSObjectReference`, until .NET disposes
 * of it (disposeJSObjectReferenceById).
 */
export function createJSObjectReference(value: unknown): { __jsObjectId: number } {
    const refId = nextJsObjectRefId++;
    jsObjectRefs.set(refId, value);
    return {"__jsObjectId": refId};
}

/** Called when .NET disposes an `IJSObjectReference`: lets go of the object held for that handle. */
export function disposeJSObjectReferenceById(id: number): void {
    jsObjectRefs.delete(id);
}

/**
 * Walks a dotted JS path on the given target (typically window).
 *
 * Returns [parentObject, lastSegment] so the caller can preserve `this` when calling methods (e.g.
 * sessionStorage.setItem must run with sessionStorage as `this`). Returns null on miss — caller throws.
 */
export function resolveIdentifier(target: unknown, identifier: string): [Record<string, unknown>, string] | null {
    if (typeof identifier !== "string" || identifier.length === 0) return null;
    const parts = identifier.split(".");
    let parent = target as Record<string, unknown> | null | undefined;
    for (let i = 0; i < parts.length - 1; i++) {
        if (parent == null) return null;
        parent = parent[parts[i]] as Record<string, unknown> | null | undefined;
    }
    if (parent == null) return null;
    return [parent, parts[parts.length - 1]];
}

/**
 * The JSON.parse reviver for an invoke's args: the inverse of the .NET side's placeholder write.
 *
 * Replaces {__jsObjectId:<id>} with the live JS object, {__raskCb__:<id>} with a function that calls
 * back into .NET, and {__raskRef__:"id"} with the live DOM element. Skips other shapes.
 */
export function jsonReviver(_key: string, value: unknown): unknown {
    if (value && typeof value === "object") {
        // Tested before it is trusted: the reviver runs on server-supplied JSON.
        const shape = value as { __jsObjectId?: number; __raskRef__?: string; __raskCb__?: number };
        if (typeof shape.__jsObjectId === "number") {
            return jsObjectRefs.get(shape.__jsObjectId);
        }
        if (typeof shape.__raskCb__ === "number") {
            return scopedCallback(shape.__raskCb__);
        }
        // ElementRef: {"__raskRef__":"id"} -> the live DOM element (or null if not in the DOM).
        // CSS.escape the id so a value carrying a quote/bracket can't break out of the
        // attribute selector or match an unintended element (defense-in-depth — ids are
        // framework-minted, but the reviver runs on server-supplied JSON).
        if (typeof shape.__raskRef__ === "string") {
            return document.querySelector(`[data-rask-ref="${CSS.escape(shape.__raskRef__)}"]`);
        }
    }
    return value;
}

/**
 * A C# Callback handed to a component's scoped script (ScopedScript.Callback): each call goes back to
 * .NET with its arguments. What it returns settles once .NET has taken the call — or, for a callback the
 * browser awaits (a lock's), once the handler has finished.
 */
export function scopedCallback(id: number): (...args: unknown[]) => Promise<void> {
    return (...args: unknown[]) =>
        window.DotNet.invokeMethodAsync("Rask.Core", "RaskScopedCallback", id, args)
            .then(() => undefined, (e: unknown) => console.error("[Rask] scoped-script callback failed", e));
}

/**
 * Runs one invoke and hands its outcome to `reply` — always exactly once, asynchronously.
 *
 * `targetInstanceId` 0 resolves `identifier` against `window`; anything else names a kept JS object
 * to resolve it against instead.
 */
export function invokeJs(
    identifier: string,
    argsJson: string | null | undefined,
    resultType: number,
    targetInstanceId: number,
    reply: JsInvokeReply): void {
    Promise.resolve().then(() => {
        let args;
        try {
            args = JSON.parse(argsJson || "[]", jsonReviver);
        } catch (e) {
            throw new Error(`Failed to parse argsJson: ${e instanceof Error ? e.message : String(e)}`);
        }

        let target: unknown = window;
        if (targetInstanceId !== 0) {
            target = jsObjectRefs.get(targetInstanceId);
            if (!target) throw new Error(`Unknown JS object reference: ${targetInstanceId}`);
        }

        const resolved = resolveIdentifier(target, identifier);
        if (!resolved) throw new Error(`Could not find '${identifier}' on target`);
        const parent = resolved[0];
        const fn = parent[resolved[1]];

        // Identifier names a property (not a method) — return its value. This is
        // how blazor handles e.g. `localStorage.length`.
        return (typeof fn === "function") ? fn.apply(parent, args) : fn;
    }).then((value) => {
        // Mirrors Microsoft.JSInterop.JSCallResultType:
        //   0 = Default            — ship the value as-is.
        //   1 = JSObjectReference  — mint a handle id, send {__jsObjectId:<id>}.
        //   2 = JSStreamReference  — not supported yet; fall through to Default.
        //   3 = JSVoidResult       — drop the value, only the success ack matters.
        if (resultType === 3) {
            reply(true, null);
            return;
        }
        if (resultType === 1) {
            reply(true, createJSObjectReference(value));
            return;
        }
        reply(true, value);
    }).catch((err) => {
        reply(false, null, (err && err.message) || String(err));
    });
}
