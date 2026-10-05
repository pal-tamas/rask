// Node-driven fixture for Rask.Web's browser-quirk patches (src/Rask.Core/Resources/rask-web-patches.ts), each driven
// against a stub of the browser that has the quirk. The C# test (WebPatchesTests) runs this in a node subprocess and
// asserts the JSON on stdout.

import { raskWebArm, raskWebCall, raskWebGlobalName, raskWebInstallPrompt, raskWebListened } from "../../../src/Rask.Core/Resources/rask-web-patches.js";

type Any = Record<string, unknown>;

function define(name: string, value: unknown): void {
    Object.defineProperty(globalThis, name, {value, configurable: true, writable: true});
}

const tick = () => new Promise(resolve => setTimeout(resolve, 0));

// ---- the stub browser ----------------------------------------------------------------------------------------------

// The window's events: node's globalThis is no EventTarget, so it borrows one.
const windowEvents = new EventTarget();
define("addEventListener", windowEvents.addEventListener.bind(windowEvents));
define("removeEventListener", windowEvents.removeEventListener.bind(windowEvents));
define("dispatchEvent", windowEvents.dispatchEvent.bind(windowEvents));

// The page's visibility, which the wake lock follows.
const page = new EventTarget() as EventTarget & { visibilityState: string };
page.visibilityState = "visible";
define("document", page);

function show(state: "visible" | "hidden"): Promise<unknown> {
    page.visibilityState = state;
    page.dispatchEvent(new Event("visibilitychange"));
    return tick();
}

// A browser's wake lock: each request is a new sentinel, released by the page going hidden or by release().
class StubSentinel {
    released = false;

    release(): Promise<void> {
        this.released = true;
        return Promise.resolve();
    }
}

class WakeLock {
    requests: string[] = [];
    refuse = false;

    request(type: string): Promise<StubSentinel> {
        this.requests.push(type);
        return this.refuse ? Promise.reject(new Error("NotAllowedError")) : Promise.resolve(new StubSentinel());
    }
}

define("WakeLock", WakeLock);

// Chromium's install prompt: prompt() answers {outcome, platform}, where MDN says {userChoice}.
class BeforeInstallPromptEvent extends Event {
    prompted = 0;

    prompt(): Promise<{ outcome: string; platform: string }> {
        this.prompted++;
        return Promise.resolve({outcome: "accepted", platform: "web"});
    }
}

define("BeforeInstallPromptEvent", BeforeInstallPromptEvent);

// ---- exercise ------------------------------------------------------------------------------------------------------

async function wakeLock(): Promise<Any> {
    const lock = new WakeLock();
    const sentinel = (await raskWebCall(lock, "request")!([])) as EventTarget & { released: boolean; type: string; release(): Promise<void> };
    let releases = 0;
    sentinel.addEventListener("release", () => releases++);
    const held = (sentinel as unknown as { held: StubSentinel }).held;

    // The browser lets go when the page is hidden; the sentinel asks again when it is visible.
    held.released = true;
    await show("hidden");
    const requestsWhileHidden = lock.requests.length;
    await show("visible");
    const requestsAfterVisible = lock.requests.length;
    const releasedAfterVisible = sentinel.released;

    await sentinel.release();
    const releasedAfterRelease = sentinel.released;
    await show("hidden");
    await show("visible");

    // A second one, which the browser refuses back.
    const refused = new WakeLock();
    const lost = (await raskWebCall(refused, "request")!([])) as EventTarget & { released: boolean };
    let lostReleases = 0;
    lost.addEventListener("release", () => lostReleases++);
    (lost as unknown as { held: StubSentinel }).held.released = true;
    refused.refuse = true;
    await show("hidden");
    await show("visible");

    return {
        wakeType: sentinel.type,
        wakeRequestsWhileHidden: requestsWhileHidden,
        wakeRequestsAfterVisible: requestsAfterVisible,
        wakeReleasedAfterVisible: releasedAfterVisible,
        wakeReleasedAfterRelease: releasedAfterRelease,
        wakeReleaseEvents: releases,
        wakeRequestsAfterRelease: lock.requests.length,
        wakeRefusedReleased: lost.released,
        wakeRefusedReleaseEvents: lostReleases,
        wakeOtherCallUnpatched: raskWebCall(lock, "release") === undefined && raskWebCall({}, "request") === undefined,
    };
}

async function speechRecognition(): Promise<Any> {
    const missing = raskWebGlobalName(globalThis, "SpeechRecognition");
    define("webkitSpeechRecognition", class {});
    const prefixed = raskWebGlobalName(globalThis, "SpeechRecognition");
    define("SpeechRecognition", class {});
    const unprefixed = raskWebGlobalName(globalThis, "SpeechRecognition");
    const elsewhere = raskWebGlobalName({webkitSpeechRecognition: 1}, "SpeechRecognition");
    return {
        speechWhenMissing: missing,
        speechWhenPrefixed: prefixed,
        speechWhenUnprefixed: unprefixed,
        speechOnAnotherObject: elsewhere,
    };
}

async function installPrompt(): Promise<Any> {
    const unavailableBeforeBoot = await raskWebInstallPrompt();
    raskWebArm(globalThis as unknown as Window);
    const fired = new BeforeInstallPromptEvent("beforeinstallprompt");
    dispatchEvent(fired);

    // A subscriber that comes after the event gets it, a tick later.
    const replayed: Event[] = [];
    raskWebListened(globalThis as unknown as EventTarget, "beforeinstallprompt", e => replayed.push(e));
    const replayedAtOnce = replayed.length;
    await tick();
    const other: Event[] = [];
    raskWebListened(globalThis as unknown as EventTarget, "click", e => other.push(e));
    await tick();

    // Prompting it spends it, and its answer is MDN's.
    const answer = await raskWebCall(fired, "prompt")!([]);
    const late: Event[] = [];
    raskWebListened(globalThis as unknown as EventTarget, "beforeinstallprompt", e => late.push(e));
    await tick();
    const unavailableWhenSpent = await raskWebInstallPrompt();

    // Trigger.Install's click shows a fresh one; appinstalled leaves nothing to show.
    dispatchEvent(new BeforeInstallPromptEvent("beforeinstallprompt"));
    const triggered = await raskWebInstallPrompt();
    dispatchEvent(new BeforeInstallPromptEvent("beforeinstallprompt"));
    dispatchEvent(new Event("appinstalled"));
    const afterInstalled = await raskWebInstallPrompt();

    return {
        installUnavailableBeforeBoot: unavailableBeforeBoot,
        installReplayedAtOnce: replayedAtOnce,
        installReplayedSame: replayed.length === 1 && replayed[0] === fired,
        installOtherEventsNotReplayed: other.length,
        installAnswer: answer,
        installPrompted: fired.prompted,
        installReplayedWhenSpent: late.length,
        installUnavailableWhenSpent: unavailableWhenSpent,
        installTriggered: triggered,
        installAfterInstalled: afterInstalled,
    };
}

async function run(): Promise<Any> {
    return {...(await wakeLock()), ...(await speechRecognition()), ...(await installPrompt())};
}

run().then(
    (result) => console.log(JSON.stringify(result)),
    (error) => {
        console.error(error);
        process.exit(1);
    });
