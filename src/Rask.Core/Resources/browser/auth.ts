// Accounts — the app's own /api/auth endpoints.
//
// See ./geolocation.ts for the two rules every module in this directory follows. This one has no
// platform API to borrow names from, so like ./cookies.ts it is named for what it does.
//
// It is the TypeScript side of the same contract Rask's C# clients speak: the paths, the header and
// the shapes come from Rask.Core.Authentication.AuthApi, so a front end and a component are talking
// to one API rather than to two that happen to agree today.
//
// WORKS ON A SERVER TOO, and that is deliberate rather than incidental. A meta framework's SSR pass
// runs in node, where fetch exists but there is no cookie jar and no page origin — so every call
// takes an optional `baseUrl` and `headers`, which is exactly what the callback into the C# app
// needs. See docs/meta.md.

/** Where the endpoints live, and who is asking. */
export interface AuthRequest {
    /**
     * The path the endpoints sit under. Must match the server's `AuthOptions.ApiPrefix`.
     * Defaults to `/api/auth`.
     */
    prefix?: string;
    /**
     * An absolute origin to call instead of this page's.
     *
     * Leave it unset in the browser: a same-origin request sends the `HttpOnly` cookie on its own.
     * Set it during a SERVER render to the value the host injected as `RASK_BASE_URL`, and forward
     * the visitor's cookie through {@link headers} — node has no cookie jar, so nothing is attached
     * for you.
     */
    baseUrl?: string;
    /** Extra headers. On a server render this is where the visitor's `cookie` goes. */
    headers?: Record<string, string>;
    /** Abort signal, passed through to `fetch`. */
    signal?: AbortSignal;
}

export interface RegisterCredentials {
    email: string;
    password: string;
    /**
     * The one-time token the FIRST registration needs while an app has no accounts yet. It is
     * written to the startup log, and stops being asked for the moment an account exists.
     */
    firstRunToken?: string;
}

export interface LoginCredentials {
    email: string;
    password: string;
    /** Whether the session should outlive the browser session. */
    remember?: boolean;
}

/** Who is signed in. */
export interface CurrentUser {
    id: string | null;
    email: string | null;
    roles: string[];
}

/**
 * Why a call was refused.
 *
 * `error` is the name of the server's `AuthError` — `"InvalidCredentials"`, `"LockedOut"`,
 * `"DuplicateAccount"`, `"WeakPassword"`, `"FirstRunTokenRequired"`, `"NotAllowed"`,
 * `"InvalidEmail"`, `"MissingRequestHeader"` — carried as a name rather than a number so a value
 * added later cannot silently become a different one.
 */
export interface AuthFailure {
    error: string;
    message: string | null;
}

export type AuthResult =
    | {ok: true; user: CurrentUser}
    | {ok: false; failure: AuthFailure};

const DEFAULT_PREFIX = "/api/auth";

/**
 * The header every state-changing call must carry.
 *
 * Cross-site markup — a form, an `<img>`, a `<script>` — cannot set a custom header, so requiring
 * one is what stops another origin driving these endpoints with your visitor's cookie. It is a CSRF
 * defence that costs no round-trip, layered over the `SameSite=Lax` cookie.
 */
export const REQUEST_HEADER = "X-Rask-Auth";

/** Creates an account and signs it in. */
export function register(
    credentials: RegisterCredentials,
    request?: AuthRequest,
): Promise<AuthResult> {
    return post("/register", credentials, request);
}

/** Signs an existing account in. */
export function login(credentials: LoginCredentials, request?: AuthRequest): Promise<AuthResult> {
    return post("/login", credentials, request);
}

/**
 * Signs the current visitor out.
 *
 * Resolves either way: an already-signed-out visitor is not an error, and neither is a network that
 * dropped on the way — the cookie is the server's to clear, and the next {@link me} tells the truth.
 */
export async function logout(request?: AuthRequest): Promise<void> {
    try {
        await send("/logout", "POST", undefined, request);
    } catch {
        // See the doc comment: nothing here is worth failing a sign-out over.
    }
}

/**
 * Who is signed in, or `null` when nobody is.
 *
 * `null` is the ordinary answer, not a failure — the endpoint says so with `204`, so an anonymous
 * page load does not fill your logs with 401s. A request that never reaches the server also reads as
 * `null`: anonymous closes doors rather than opening them.
 */
export async function me(request?: AuthRequest): Promise<CurrentUser | null> {
    let response: Response;

    try {
        response = await send("/me", "GET", undefined, request);
    } catch {
        return null;
    }

    if (response.status === 204 || !response.ok) {
        return null;
    }

    return (await response.json()) as CurrentUser;
}

async function post(route: string, body: unknown, request?: AuthRequest): Promise<AuthResult> {
    let response: Response;

    try {
        response = await send(route, "POST", body, request);
    } catch {
        // Never reached a server. Reported as a refusal rather than thrown, so a form submit renders
        // a message instead of an unhandled rejection.
        return {ok: false, failure: {error: "InvalidCredentials", message: null}};
    }

    if (response.ok) {
        return {ok: true, user: (await response.json()) as CurrentUser};
    }

    let failure: AuthFailure = {error: "InvalidCredentials", message: null};

    try {
        failure = (await response.json()) as AuthFailure;
    } catch {
        // A proxy can answer with something that is not this app's problem document.
    }

    return {ok: false, failure};
}

function send(
    route: string,
    method: "GET" | "POST",
    body: unknown,
    request?: AuthRequest,
): Promise<Response> {
    const prefix = request?.prefix ?? DEFAULT_PREFIX;
    const url = (request?.baseUrl ?? "").replace(/\/+$/, "") + prefix + route;

    const headers: Record<string, string> = {...(request?.headers ?? {})};

    // GET /me changes nothing, so it does not need the CSRF header — and a server render that only
    // reads the current user should not have to know about one.
    if (method === "POST") {
        headers[REQUEST_HEADER] = "1";
    }

    if (body !== undefined) {
        headers["content-type"] = "application/json";
    }

    return fetch(url, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: request?.signal,
    });
}
