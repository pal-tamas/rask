// Node-driven fixture for the shared browser layer (src/Rask.Core/Resources/browser/*.ts).
//
// Two things are under test here, and the second one is the reason this runs in NODE rather than a
// browser.
//
//  1. BEHAVIOUR. These modules were extracted out of rask-api.ts, where they had been driven only by
//     C# through a dotted IJSRuntime identifier. An extraction that compiles and bundles proves
//     nothing about whether the cookie string is still built the same way — so the mapping of every
//     moved function is asserted here.
//
//  2. SSR SAFETY, by construction. Node has no window and no document. ES imports are hoisted and
//     evaluated before any statement below, so if ANY module in ./browser/ touched one at import
//     time, this process would die on the import with a ReferenceError and print nothing. That is
//     precisely the failure a Next or Nuxt server render would hit, and it is why the rule is "side
//     effects live in globals.ts and nowhere else". Note what is deliberately NOT imported:
//     ./globals.ts, which assigns to window and is the framework's own entry point.
//
//     Node DOES define `navigator` (>= 21), which is the sharper lesson: a module cannot decide it is
//     on a server by finding no navigator. Every capability check has to name the capability —
//     `navigator.geolocation`, `navigator.storage.estimate` — never merely the object holding it.
//
// The C# test (BrowserModuleTests) runs this in a node subprocess and asserts the JSON on stdout.

import * as auth from "../../../src/Rask.Core/Resources/browser/auth.js";
import * as cookies from "../../../src/Rask.Core/Resources/browser/cookies.js";
import * as geolocation from "../../../src/Rask.Core/Resources/browser/geolocation.js";
import * as mediaQuery from "../../../src/Rask.Core/Resources/browser/mediaQuery.js";
import * as networkInformation from "../../../src/Rask.Core/Resources/browser/networkInformation.js";
import * as storageManager from "../../../src/Rask.Core/Resources/browser/storageManager.js";

// Reaching this line at all is assertion (2): every import above evaluated with no DOM present.
const importedWithoutADom = true;

type Any = Record<string, unknown>;

// defineProperty rather than assignment: node >= 21 ships its own `navigator` global as a
// getter-only accessor, so `globalThis.navigator = …` throws. Worth knowing beyond this fixture —
// it means a module cannot rely on `typeof navigator === "undefined"` to detect a server, which is
// why each function below checks for the capability it needs rather than for a DOM.
function define(name: string, value: unknown): void {
    Object.defineProperty(globalThis, name, {value, configurable: true, writable: true});
}

// --- the stub DOM, installed AFTER the imports ------------------------------------------------

const cookieWrites: string[] = [];
const documentStub: Any = {};
Object.defineProperty(documentStub, "cookie", {
    get: () => "a=1; token=he%20llo; empty=",
    set: (value: string) => {
        cookieWrites.push(value);
    }
});
define("document", documentStub);

const mediaQueries: string[] = [];
define("window", {
    matchMedia: (query: string) => {
        mediaQueries.push(query);
        return {matches: query.indexOf("dark") >= 0};
    }
});

let clears = 0;
define("navigator", {
    geolocation: {
        getCurrentPosition: (
            ok: (p: unknown) => void,
            _fail: (e: unknown) => void,
            opts: Record<string, unknown>) => {
            ok({
                coords: {
                    latitude: 51.5,
                    longitude: -0.12,
                    accuracy: 12,
                    altitude: null,
                    altitudeAccuracy: null,
                    heading: null,
                    speed: null
                },
                timestamp: 1234,
                requested: opts
            });
        },
        watchPosition: (ok: (p: unknown) => void) => {
            ok({
                coords: {
                    latitude: 1,
                    longitude: 2,
                    accuracy: 3,
                    altitude: null,
                    altitudeAccuracy: null,
                    heading: null,
                    speed: null
                },
                timestamp: 99
            });
            return 7;
        },
        clearWatch: () => {
            clears++;
        }
    },
    // Deliberately the Mozilla-prefixed one: the unprefixed navigator.connection is absent, which is
    // the shape Firefox actually presents, and the fallback chain has to find it.
    mozConnection: {effectiveType: "3g", saveData: true},
    storage: {estimate: () => Promise.resolve({})}
});

// --- exercise -----------------------------------------------------------------------------------

async function run(): Promise<Any> {
    const fix = await geolocation.getCurrentPosition({enableHighAccuracy: true, timeoutMs: 5000});

    const watched: unknown[] = [];
    const stop = geolocation.watchPosition((f) => watched.push(f));
    stop();
    stop(); // idempotent: a second stop must not clear a second time

    cookies.set("token", "he llo", {maxAgeSeconds: 60, path: "/", sameSite: "Lax", secure: true});
    cookies.remove("token", "/app");

    const estimate = await storageManager.estimate();

    // ---- auth.ts ---------------------------------------------------------------------------------
    // fetch is a global rather than a DOM member, so replacing it drives this module with no server.
    // Each capture records what the module ASKED for.
    const realFetch = globalThis.fetch;
    let lastRequest: Any = {};

    function captureFetch(status: number, body: unknown): void {
        define("fetch", (url: string, init: Any) => {
            lastRequest = {url, method: init.method, headers: init.headers, body: init.body};
            return Promise.resolve({
                ok: status >= 200 && status < 300,
                status,
                json: () => Promise.resolve(body)
            });
        });
    }

    captureFetch(200, {id: "u1", email: "ada@example.com", roles: ["admin"]});
    await auth.login({email: "ada@example.com", password: "pw"});
    const authLoginRequest = lastRequest;

    // A SERVER render: an absolute base URL, and the visitor's cookie forwarded by hand because node
    // has no cookie jar. GET /me carries no CSRF header — it changes nothing.
    captureFetch(204, null);
    await auth.me({baseUrl: "http://127.0.0.1:8080/", headers: {cookie: "rask.auth=abc"}});
    const authMeRequest = lastRequest;

    // A transport that never reaches a server: sign-out still resolves, and "who is signed in" reads
    // as nobody rather than throwing.
    define("fetch", () => Promise.reject(new Error("offline")));
    await auth.logout();
    const authLogoutOnFailureResolves = true;
    const authMeOnFailureIsNull = (await auth.me()) === null;

    // A refusal carries the server's error NAME through unchanged.
    captureFetch(401, {error: "LockedOut", message: "Too many attempts."});
    const refused = await auth.login({email: "ada@example.com", password: "wrong"});
    const authFailureFromProblemDocument = refused.ok ? null : refused.failure;

    define("fetch", realFetch);

    return {
        // Auth: the request built, not a round trip — the URL, the CSRF header on a POST and its
        // absence on a GET, and the cookie a server render forwards itself.
        authLoginRequest,
        authMeRequest,
        authLogoutOnFailureResolves,
        authMeOnFailureIsNull,
        authFailureFromProblemDocument,

        importedWithoutADom,

        // Geolocation flattens GeolocationPosition and carries the timestamp across.
        fixLatitude: fix.latitude,
        fixAltitudeIsNull: fix.altitude === null,
        fixTimestampMs: fix.timestampMs,

        // A subscription hands back a stop function rather than an id, and stopping twice clears once.
        watchedCount: watched.length,
        clears,

        // Cookies: reads decode, writes build the assignment string option by option.
        cookieRead: cookies.get("token"),
        cookieMissing: cookies.get("nope"),
        cookieAll: cookies.getAll(),
        cookieSetWrite: cookieWrites[0],
        cookieDeleteWrite: cookieWrites[1],

        // Media queries: the convenience wrappers must ask the real query strings.
        prefersDark: mediaQuery.prefersDark(),
        prefersReducedMotion: mediaQuery.prefersReducedMotion(),
        mediaQueries,

        // Network info resolves through the vendor-prefixed fallback and defaults the numbers.
        network: networkInformation.current(),
        networkSupported: networkInformation.isSupported(),

        // An estimate with neither figure present reports zeroes rather than undefined.
        estimate
    };
}

run().then(
    (result) => console.log(JSON.stringify(result)),
    (error) => {
        console.error(error);
        process.exit(1);
    });
