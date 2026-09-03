// Web Crypto — crypto and crypto.subtle.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Both of these need a SECURE CONTEXT: `crypto.subtle` is undefined on plain http, localhost aside.
// That is a deployment failure rather than a browser one, and it surfaces here as a TypeError on the
// first call rather than as a support check returning false.

/** A cryptographically random UUID (v4). */
export function randomUuid(): string {
    return crypto.randomUUID();
}

/** `length` cryptographically random bytes. */
export function randomBytes(length: number): Uint8Array {
    return crypto.getRandomValues(new Uint8Array(length));
}

/**
 * A digest of `text`, lowercase hex. `algorithm` is what SubtleCrypto accepts — "SHA-256",
 * "SHA-384", "SHA-512". (Not "SHA-1": SubtleCrypto still implements it, and you should not use it.)
 *
 * Hex is built with a lookup and a length test rather than `padStart` on a radix string, which is
 * both faster over a digest-sized array and free of the surprise that `padStart` is doing string
 * arithmetic in a hot loop.
 */
export async function digestHex(algorithm: AlgorithmIdentifier, text: string): Promise<string> {
    const data = new TextEncoder().encode(text);
    const buf = await crypto.subtle.digest(algorithm, data);
    const bytes = new Uint8Array(buf);
    let hex = "";
    for (let i = 0; i < bytes.length; i++) {
        const h = bytes[i].toString(16);
        hex += (h.length === 1 ? "0" : "") + h;
    }
    return hex;
}
