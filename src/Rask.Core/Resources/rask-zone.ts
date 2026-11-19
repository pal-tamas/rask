// The reader's time zone, told to the server (Reader.TimeZone in C#).
//
// The browser knows the zone on the reader's wall; the server only knows its own. So the runtime keeps the IANA id
// (Intl's "Europe/Budapest") in a first-party cookie the moment it loads — before the live connection opens, which is
// a request that carries it — and every request after carries it too: the socket's upgrade, an HTTP stream's posts,
// the next page. A page's very first render, before the runtime has ever run in this browser, is drawn in the app's
// zone; the session takes the reader's as it connects.
//
// Imported for its side effect by the server runtime (Rask.Server's rask.ts), ahead of its first connection. A
// browser app needs none of it: its own clock is already in the reader's zone.

const ZONE_COOKIE = "rask-tz";

function tellZone(): void {
    // No document with cookies (a worker, a test's stand-in document), no Intl: nothing to tell, and nothing breaks.
    if (typeof document === "undefined" || typeof document.cookie !== "string" || typeof Intl === "undefined") {
        return;
    }
    let zone: string | undefined;
    try {
        zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    } catch {
        return;
    }
    if (!zone) {
        return;
    }
    const value = encodeURIComponent(zone);
    if (document.cookie.split("; ").indexOf(ZONE_COOKIE + "=" + value) >= 0) {
        return;
    }
    const secure = location.protocol === "https:" ? "; Secure" : "";
    document.cookie = ZONE_COOKIE + "=" + value + "; Path=/; Max-Age=31536000; SameSite=Lax" + secure;
}

tellZone();
