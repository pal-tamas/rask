// The script fixture host itself (ScriptFixture.Run, web-globals.js): the virtual clock, the web globals the shipped
// modules lean on, and every way a run must refuse to report a result it cannot vouch for. ScriptHostTests passes
// the scenario as the one argument and asserts what comes back — or that nothing does.

import {args, nextTask, report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";

const [scenario] = args;

async function clock() {
    const order: string[] = [];
    const startedAt = Date.now();
    const startedNow = performance.now();
    setTimeout(() => order.push("later"), 50);
    setTimeout(() => order.push("sooner"), 10);
    setTimeout(() => order.push("zero"), 0);
    setTimeout(() => order.push("one"), 1);
    queueMicrotask(() => order.push("microtask"));
    let ticks = 0;
    const interval = setInterval(() => {
        if (++ticks === 3) clearInterval(interval);
    }, 20);
    const cancelled = setTimeout(() => order.push("cancelled"), 5);
    clearTimeout(cancelled);
    await new Promise((resolve) => setTimeout(resolve, 300));
    return {
        order,
        ticks,
        dateMoved: Date.now() - startedAt,
        performanceMoved: performance.now() - startedNow,
    };
}

async function globals() {
    const encoded = new TextEncoder().encode("aé€😀");
    const streaming = new TextDecoder();
    const split = streaming.decode(encoded.subarray(0, 2), {stream: true}) + streaming.decode(encoded.subarray(2));

    const url = new URL("../b/c?x=1 2#h", "https://Example.test:443/a/d/e");
    url.searchParams.append("y", "a&b");
    const query = new URLSearchParams("a=1&b=two+words&a=3");

    const headers = new Headers({"Content-Type": "text/plain"});
    headers.append("x-many", "1");
    headers.append("X-Many", "2");

    const target = new EventTarget();
    let heard = 0;
    target.addEventListener("ping", () => heard++, {once: true});
    target.dispatchEvent(new Event("ping"));
    target.dispatchEvent(new Event("ping"));

    const controller = new AbortController();
    let aborted = "";
    controller.signal.addEventListener("abort", () => (aborted = (controller.signal.reason as Error).name));
    controller.abort();

    const body = new ReadableStream<Uint8Array>({
        start(c) {
            c.enqueue(new TextEncoder().encode("streamed "));
            c.enqueue(new TextEncoder().encode("body"));
            c.close();
        },
    });

    return {
        bytes: Array.from(encoded),
        split,
        url: url.href,
        origin: url.origin,
        pathname: url.pathname,
        query: [query.getAll("a"), query.get("b"), query.toString()],
        headers: [headers.get("content-type"), headers.get("x-many"), [...headers.keys()]],
        heard,
        aborted,
        base64: [btoa("hi there"), atob("aGkgdGhlcmU=")],
        cloned: structuredClone({when: new Date(0), list: [1, {deep: true}], map: new Map([["k", 1]])}).map instanceof Map,
        response: await new Response(body, {status: 201}).text(),
        json: await Response.json({ok: true}).json(),
        stackHead: (new RangeError("bad range").stack ?? "").split("\n")[0],
    };
}

switch (scenario) {
    case "clock":
        report(await clock());
        break;
    case "globals":
        report(await globals());
        break;
    case "throws":
        throw new Error("the fixture's own stub broke");
    case "rejects":
        void Promise.reject(new Error("nobody handled this"));
        await nextTask();
        report({});
        break;
    case "twice":
        report({});
        report({});
        break;
    case "endless":
        setInterval(() => {}, 1000);
        break;
    case "silent":
        break;
    default:
        throw new Error(`ScriptHostFixture has no scenario '${scenario}'.`);
}
