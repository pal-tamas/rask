// Passkeys — navigator.credentials with a PublicKeyCredential.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// This is the module that most earns its place. The platform takes and returns ArrayBuffers, while
// every relying-party backend on earth speaks base64url — so the API you actually want is the one
// below, and writing it yourself means getting padding, the "-_" alphabet and six different binary
// fields right before anything works at all.
//
// A cancelled or timed-out ceremony resolves NULL rather than throwing. The platform reports both as
// NotAllowedError, which is indistinguishable from a real failure at a call site, and a user closing
// the passkey sheet is not an error.

export interface CredentialDescriptor {
    /** "public-key" is the only type the spec defines. */
    type?: string;
    /** base64url. */
    id: string;
    transports?: string[];
}

export interface RelyingParty {
    id?: string;
    name: string;
}

export interface UserInfo {
    /** base64url. An opaque handle — never an email address or anything else personal. */
    id: string;
    name: string;
    displayName: string;
}

export interface CreateOptions {
    /** base64url, from your server. Must be single-use. */
    challenge: string;
    rp: RelyingParty;
    user: UserInfo;
    /** Defaults to ES256 and RS256, which is what authenticators actually implement. */
    pubKeyCredParams?: PublicKeyCredentialParameters[] | null;
    timeoutMs?: number | null;
    attestation?: AttestationConveyancePreference | null;
    authenticatorSelection?: AuthenticatorSelectionCriteria | null;
    /** Credentials already registered, so the authenticator refuses to enrol a duplicate. */
    excludeCredentials?: CredentialDescriptor[] | null;
}

export interface GetOptions {
    /** base64url, from your server. Must be single-use. */
    challenge: string;
    timeoutMs?: number | null;
    rpId?: string | null;
    /** Empty or omitted asks the authenticator to offer whatever it has (discoverable credentials). */
    allowCredentials?: CredentialDescriptor[] | null;
    userVerification?: UserVerificationRequirement | null;
}

/** A newly registered credential. Every binary field is base64url, ready to POST. */
export interface CreatedCredential {
    id: string;
    rawId: string;
    type: string;
    clientDataJson: string;
    attestationObject: string;
    transports: string[] | null;
}

/** A sign-in assertion. Every binary field is base64url, ready to POST. */
export interface AssertedCredential {
    id: string;
    rawId: string;
    type: string;
    clientDataJson: string;
    authenticatorData: string;
    signature: string;
    userHandle: string | null;
}

function b64urlToBuf(s: string): ArrayBuffer {
    let pad = "";
    if (s.length % 4 !== 0) {
        for (let i = 0; i < 4 - (s.length % 4); i++) {
            pad += "=";
        }
    }
    const bin = atob(s.split("-").join("+").split("_").join("/") + pad);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) {
        bytes[i] = bin.charCodeAt(i);
    }
    return bytes.buffer;
}

function bufToB64url(buf: ArrayBuffer): string {
    const bytes = new Uint8Array(buf);
    let bin = "";
    for (let i = 0; i < bytes.length; i++) {
        bin += String.fromCharCode(bytes[i]);
    }
    // Strip the "=" padding (base64 uses it only as trailing padding), then make it URL-safe.
    return btoa(bin).split("=").join("").split("+").join("-").split("/").join("_");
}

function descriptors(list: CredentialDescriptor[] | null | undefined): PublicKeyCredentialDescriptor[] {
    return (list || []).map((d) => ({
        type: (d.type || "public-key") as "public-key",
        id: b64urlToBuf(d.id),
        transports: d.transports as AuthenticatorTransport[] | undefined
    }));
}

function isCancel(e: unknown): boolean {
    return e instanceof Error && (e.name === "NotAllowedError" || e.name === "AbortError");
}

export function isSupported(): boolean {
    return typeof window !== "undefined" && !!(window.PublicKeyCredential && navigator.credentials);
}

/**
 * Whether this device has a built-in authenticator the user can verify with — Touch ID, Windows
 * Hello, a phone's screen lock. False does not mean passkeys are unavailable: a security key or a
 * nearby phone can still work.
 */
export function isPlatformAuthenticatorAvailable(): Promise<boolean> {
    if (typeof window === "undefined"
        || !window.PublicKeyCredential
        || !PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable) {
        return Promise.resolve(false);
    }
    return PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
}

/** Register a new passkey. Resolves null if the user cancels. */
export async function create(options: CreateOptions): Promise<CreatedCredential | null> {
    const publicKey: PublicKeyCredentialCreationOptions = {
        challenge: b64urlToBuf(options.challenge),
        rp: options.rp,
        user: {
            id: b64urlToBuf(options.user.id),
            name: options.user.name,
            displayName: options.user.displayName
        },
        pubKeyCredParams: (options.pubKeyCredParams && options.pubKeyCredParams.length)
            ? options.pubKeyCredParams
            : ([{type: "public-key", alg: -7}, {type: "public-key", alg: -257}] as
                PublicKeyCredentialParameters[]),
        timeout: options.timeoutMs || undefined,
        attestation: options.attestation || undefined,
        authenticatorSelection: options.authenticatorSelection || undefined,
        excludeCredentials: options.excludeCredentials
            ? descriptors(options.excludeCredentials)
            : undefined
    };

    let cred: PublicKeyCredential | null;
    try {
        cred = await navigator.credentials.create({publicKey}) as PublicKeyCredential | null;
    } catch (e) {
        if (isCancel(e)) {
            return null;
        }
        throw e;
    }
    if (!cred) {
        return null;
    }

    const attestation = cred.response as AuthenticatorAttestationResponse;
    return {
        id: cred.id,
        rawId: bufToB64url(cred.rawId),
        type: cred.type,
        clientDataJson: bufToB64url(attestation.clientDataJSON),
        attestationObject: bufToB64url(attestation.attestationObject),
        transports: attestation.getTransports ? attestation.getTransports() : null
    };
}

/** Sign in with an existing passkey. Resolves null if the user cancels. */
export async function get(options: GetOptions): Promise<AssertedCredential | null> {
    const publicKey: PublicKeyCredentialRequestOptions = {
        challenge: b64urlToBuf(options.challenge),
        timeout: options.timeoutMs || undefined,
        rpId: options.rpId || undefined,
        allowCredentials: options.allowCredentials
            ? descriptors(options.allowCredentials)
            : undefined,
        userVerification: options.userVerification || undefined
    };

    let cred: PublicKeyCredential | null;
    try {
        cred = await navigator.credentials.get({publicKey}) as PublicKeyCredential | null;
    } catch (e) {
        if (isCancel(e)) {
            return null;
        }
        throw e;
    }
    if (!cred) {
        return null;
    }

    const assertion = cred.response as AuthenticatorAssertionResponse;
    return {
        id: cred.id,
        rawId: bufToB64url(cred.rawId),
        type: cred.type,
        clientDataJson: bufToB64url(assertion.clientDataJSON),
        authenticatorData: bufToB64url(assertion.authenticatorData),
        signature: bufToB64url(assertion.signature),
        userHandle: assertion.userHandle ? bufToB64url(assertion.userHandle) : null
    };
}
