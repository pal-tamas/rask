// Cookies — document.cookie.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// document.cookie has no method surface to borrow names from — reading it yields the whole jar as one
// string and writing it appends a single entry — so these are named for what they do.

export interface CookieOptions {
    maxAgeSeconds?: number | null;
    /** A UTC date string, as `Date.prototype.toUTCString` produces. */
    expires?: string | null;
    path?: string | null;
    domain?: string | null;
    sameSite?: "Strict" | "Lax" | "None" | null;
    secure?: boolean;
}

/** One cookie's value, or null when it is not set. */
export function get(name: string): string | null {
    const prefix = encodeURIComponent(name) + "=";
    const parts = document.cookie ? document.cookie.split("; ") : [];
    for (let i = 0; i < parts.length; i++) {
        if (parts[i].indexOf(prefix) === 0) {
            return decodeURIComponent(parts[i].slice(prefix.length));
        }
    }
    return null;
}

/** Every readable cookie, as a plain object. HttpOnly cookies are not visible here, by design. */
export function getAll(): Record<string, string> {
    const out: Record<string, string> = {};
    const parts = document.cookie ? document.cookie.split("; ") : [];
    for (let i = 0; i < parts.length; i++) {
        const eq = parts[i].indexOf("=");
        if (eq > 0) {
            out[decodeURIComponent(parts[i].slice(0, eq))] = decodeURIComponent(parts[i].slice(eq + 1));
        }
    }
    return out;
}

export function set(name: string, value: string, options?: CookieOptions): void {
    let s = encodeURIComponent(name) + "=" + encodeURIComponent(value);
    if (options) {
        if (options.maxAgeSeconds != null) s += "; max-age=" + options.maxAgeSeconds;
        if (options.expires) s += "; expires=" + options.expires;
        if (options.path) s += "; path=" + options.path;
        if (options.domain) s += "; domain=" + options.domain;
        if (options.sameSite) s += "; samesite=" + options.sameSite;
        if (options.secure) s += "; secure";
    }
    document.cookie = s;
}

/**
 * Expire a cookie now.
 *
 * `path` matters: a cookie set under a path can only be deleted by naming that same path, and getting
 * it wrong fails silently — the cookie stays and a re-read still returns it.
 */
export function remove(name: string, path?: string | null): void {
    document.cookie = encodeURIComponent(name) + "=; max-age=0" + (path ? "; path=" + path : "");
}
