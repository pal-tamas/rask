// rask-client vendored from Rask.Spa.Hosting
//
// The client half of Rask.Cqrs, in TypeScript. It mirrors IDispatcher: one entry point, and the
// result type comes off the message rather than being asserted at the call site.
//
//     const greeting = await rask.dispatch(getGreeting({ name }))   // Greeting, inferred
//
// You own this file. It is refreshed on build only while the header line above is intact, so
// deleting that line forks it permanently.

/** Query, command or notification — the distinction the server enforces with a 405. */
export type MessageKind = 'query' | 'command' | 'notification'

/**
 * A message with its wire name and its result type bound together.
 *
 * `_result` is phantom: never written, never read, erased at build. It exists so `dispatch` can
 * infer `TResult` from its argument, which is what `IQuery<TResult>` does in C#. The payload is a
 * nested field rather than the envelope itself so the phantom never reaches an object literal,
 * where it would trip excess-property checks.
 */
export interface Dispatchable<TResult, TKind extends MessageKind = MessageKind> {
  readonly name: string
  readonly kind: TKind
  /** Payload properties carrying a File/Blob, in the wire index order the server assigns. */
  readonly files: readonly string[]
  readonly returnsFile: boolean
  /** The shape the answer revives against, and how many containers stand in front of it. */
  readonly result?: ShapeRef
  readonly payload: unknown
  readonly _result?: TResult
}

export interface MessageSpec<TKind extends MessageKind> {
  readonly name: string
  readonly kind: TKind
  readonly files?: readonly string[]
  readonly returnsFile?: boolean
  readonly result?: ShapeRef
}

export type MessageFactory<TPayload, TResult, TKind extends MessageKind> = ((
  payload: TPayload,
) => Dispatchable<TResult, TKind>) & { readonly messageName: string }

/**
 * Builds a message factory. Generated code calls this; you should not need to.
 *
 * The factory carries its own wire name, so cache keys and invalidation never spell it as a string
 * literal at the call site.
 */
export function message<TPayload, TResult = void, TKind extends MessageKind = MessageKind>(
  spec: MessageSpec<TKind>,
): MessageFactory<TPayload, TResult, TKind> {
  const files = spec.files ?? []
  const returnsFile = spec.returnsFile ?? false
  const factory = (payload: TPayload): Dispatchable<TResult, TKind> => ({
    name: spec.name,
    kind: spec.kind,
    files,
    returnsFile,
    result: spec.result,
    payload,
  })
  return Object.assign(factory, { messageName: spec.name })
}

/** A named shape, and how many arrays or dictionaries stand between a value and it. */
export type ShapeRef = readonly [shape: string, depth: number]

/** One shape's date-bearing properties, and the properties that lead to other shapes. */
export interface Shape {
  readonly instants: readonly string[]
  readonly nested: Readonly<Record<string, ShapeRef>>
}

let shapes: Readonly<Record<string, Shape>> = {}

/**
 * Arms date revival with the generated shape table.
 *
 * Called by the generated messages module, not by you. It is pushed in rather than imported so this
 * file never depends on generated code — a scaffolded app has to type-check before its first build
 * has produced any.
 */
export function registerShapes(table: Readonly<Record<string, Shape>>): void {
  shapes = table
}

/**
 * Turns exactly the strings the C# types called instants into `Date` objects, in place.
 *
 * Deliberately not a `JSON.parse` reviver testing every string against a date-shaped regex: that
 * converts a product code, an ETag or a free-text field that merely looks like a timestamp, and it
 * does so silently. Nothing is guessed here — the server's own type said which properties these are.
 *
 * `DateOnly`, `TimeOnly` and `TimeSpan` are left as strings on purpose, and are not in the table. A
 * calendar date is not an instant: `new Date("2026-08-25")` is UTC midnight, so anyone west of UTC
 * would render it as the 24th.
 *
 * Nothing is needed in the other direction — `JSON.stringify` already calls `Date.toJSON`, which is
 * `toISOString()`: always UTC, always with a `Z`. So a value sent from the browser is never
 * ambiguous, and a round trip normalises a C# `DateTime` with an unspecified `Kind` into UTC.
 */
export function revive<T>(value: T, ref: ShapeRef | undefined): T {
  if (ref === undefined) return value
  walk(value, ref[0], ref[1])
  return value
}

function walk(value: unknown, shape: string, depth: number): void {
  if (value === null || typeof value !== 'object') return

  if (depth > 0) {
    // An array's items or a dictionary's values, which are indistinguishable at this point and want
    // the same treatment. Object.values covers both.
    for (const item of Object.values(value as Record<string, unknown>)) {
      walk(item, shape, depth - 1)
    }
    return
  }

  const descriptor = shapes[shape]
  if (descriptor === undefined) return

  const record = value as Record<string, unknown>
  for (const property of descriptor.instants) {
    record[property] = toDates(record[property])
  }
  for (const property of Object.keys(descriptor.nested)) {
    const nested = descriptor.nested[property]
    walk(record[property], nested[0], nested[1])
  }
}

/**
 * An instant, or any nesting of arrays and dictionaries of them.
 *
 * The container depth is not carried for instants the way it is for shapes, because it does not need
 * to be: a string is unmistakable, so the walk can simply stop at one.
 */
function toDates(value: unknown): unknown {
  if (typeof value === 'string') return new Date(value)
  if (Array.isArray(value)) return value.map(toDates)
  if (value !== null && typeof value === 'object') {
    const record = value as Record<string, unknown>
    for (const key of Object.keys(record)) record[key] = toDates(record[key])
  }
  return value
}

/**
 * The constants the two halves of the transport must agree on, mirroring
 * Rask.Cqrs.RemoteEndpointDefaults. Generated code re-exports these read from the C# constants, so
 * a server that moves its route prefix moves the client with it.
 */
export const wire = {
  routePrefix: '/_rask/cqrs/request',
  messageQueryParameter: 'm',
  /** Its only job is CSRF: no form, <img> or <script> can set a custom header. */
  requestHeader: 'X-Rask-Cqrs',
  requestHeaderValue: '1',
  /** Above this the client posts instead, so a long query cannot 414 behind somebody's proxy. */
  maxQueryUrlLength: 2000,
  uploadSegment: 'upload',
  uploadHeader: 'X-Rask-Upload',
  uploadFileHeader: 'X-Rask-Upload-File',
  uploadOffsetHeader: 'X-Rask-Upload-Offset',
  uploadNameHeader: 'X-Rask-Upload-Name',
  uploadTypeHeader: 'X-Rask-Upload-Type',
  chunkedUploadThreshold: 4 * 1024 * 1024,
  uploadChunkSize: 1024 * 1024,
} as const

export interface UploadProgress {
  readonly file: number
  readonly fileName: string
  readonly sent: number
  readonly total: number | null
}

export interface CallOptions {
  signal?: AbortSignal
  timeoutMs?: number
  headers?: Record<string, string>
  onUploadProgress?: (progress: UploadProgress) => void
}

/** A file streamed back by a query whose handler returns a FileDownload. */
export interface RaskDownload {
  readonly fileName: string
  readonly contentType: string
  readonly size: number | null
  blob(): Promise<Blob>
  /** Hands the file to the browser's save flow. */
  save(fileName?: string): Promise<void>
}

/**
 * A message the server refused or could not answer — the TypeScript twin of
 * RemoteDispatchException, carrying the RFC 9457 problem document the endpoint returns.
 */
export class RaskDispatchError extends Error {
  /** HTTP status, or 0 when the request never reached the server. */
  readonly status: number
  readonly messageName: string
  readonly title?: string
  readonly detail?: string
  readonly problemType?: string

  constructor(
    messageName: string,
    status: number,
    init: { title?: string; detail?: string; problemType?: string; cause?: unknown } = {},
  ) {
    super(
      status === 0
        ? `'${messageName}' could not reach the server.`
        : `'${messageName}' failed on the server: ${status} ${init.title ?? ''}`.trimEnd() + '.',
      { cause: init.cause },
    )
    this.name = 'RaskDispatchError'
    this.status = status
    this.messageName = messageName
    this.title = init.title
    this.detail = init.detail
    this.problemType = init.problemType
  }

  get isNetwork(): boolean {
    return this.status === 0
  }

  get isUnauthorized(): boolean {
    return this.status === 401
  }

  get isForbidden(): boolean {
    return this.status === 403
  }

  /**
   * Almost always a stale generated client rather than a routing bug: the server does not expose a
   * message by that name. Rebuild the server to regenerate the contracts.
   *
   * Note the server answers 401 *before* it judges the name, so that an anonymous caller cannot
   * enumerate every message one guess at a time — which is why a 401 must never be read as this.
   */
  get isUnknownMessage(): boolean {
    return this.status === 404
  }

  get isConflict(): boolean {
    return this.status === 409
  }

  get isTooLarge(): boolean {
    return this.status === 413
  }
}

export interface DispatchRequest {
  readonly name: string
  readonly kind: MessageKind
  readonly files: readonly string[]
  readonly returnsFile: boolean
  readonly payload: unknown
  readonly options: CallOptions
}

/**
 * How a dispatch actually travels. Mirrors IRemoteDispatch sitting under IDispatcher — and it is
 * what makes the dispatcher testable with no network.
 */
export interface RaskTransport {
  send(request: DispatchRequest): Promise<unknown>
}

export interface HttpTransportOptions {
  /** Defaults to the document's base URL, so a sub-path deploy needs no configuration. */
  baseUrl?: string
  fetch?: typeof globalThis.fetch
  /** Matching RaskCqrsClientOptions.Timeout. */
  timeoutMs?: number
  /** Last chance to add a bearer token or a tracing header. */
  onRequest?: (request: Request) => Request | Promise<Request>
  onUnauthorized?: (error: RaskDispatchError) => void
}

export interface RaskDispatcher {
  dispatch<TResult>(message: Dispatchable<TResult>, options?: CallOptions): Promise<TResult>
}

function defaultBaseUrl(): string {
  // Vite exposes the deploy prefix here. Falls back to '' outside a bundler (tests, SSR).
  const meta = import.meta as unknown as { env?: { BASE_URL?: string } }
  const base = meta.env?.BASE_URL ?? ''
  return base === '/' ? '' : base.replace(/\/$/, '')
}

function readProblem(text: string): { type?: string; title?: string; detail?: string } {
  try {
    const body = JSON.parse(text) as Record<string, unknown>

    // Bracket access, not `body.type`. This file is vendored into clients whose tsconfig is not ours,
    // and Angular's turns on `noPropertyAccessFromIndexSignature` — under which reading a Record with a
    // dot is an error. Bracket access compiles everywhere and means exactly the same thing.
    const read = (key: string): string | undefined =>
      typeof body[key] === 'string' ? (body[key] as string) : undefined

    return { type: read('type'), title: read('title'), detail: read('detail') }
  } catch {
    // A malformed body is not worth losing the status code over — the same call the C# client makes.
    return {}
  }
}

/** The filename from a Content-Disposition, preferring the RFC 5987 form the server writes. */
export function parseContentDisposition(header: string | null): string | null {
  if (!header) return null
  const star = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header)
  if (star) {
    try {
      return decodeURIComponent(star[1])
    } catch {
      return star[1]
    }
  }
  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/.exec(header)
  return plain ? (plain[2] ?? plain[1]).trim() : null
}

/** Splits a file into [start, end) chunks, resuming from an offset the server already holds. */
export function planChunks(size: number, chunkSize: number, from = 0): Array<[number, number]> {
  const chunks: Array<[number, number]> = []
  for (let start = from; start < size; start += chunkSize) {
    chunks.push([start, Math.min(start + chunkSize, size)])
  }
  return chunks
}

/** Whether a query is short enough to travel as a GET, and the URL if so. */
export function buildQueryUrl(
  base: string,
  name: string,
  json: string,
  max: number = wire.maxQueryUrlLength,
): { method: 'GET' | 'POST'; url: string } {
  const path = `${base}${wire.routePrefix}/${encodeURIComponent(name)}`
  const url = `${path}?${wire.messageQueryParameter}=${encodeURIComponent(json)}`
  return url.length <= max ? { method: 'GET', url } : { method: 'POST', url: path }
}

export function httpTransport(options: HttpTransportOptions = {}): RaskTransport {
  const base = options.baseUrl ?? defaultBaseUrl()
  const doFetch = options.fetch ?? globalThis.fetch.bind(globalThis)

  async function call(
    request: DispatchRequest,
    method: string,
    url: string,
    body: BodyInit | undefined,
    extraHeaders: Record<string, string>,
  ): Promise<Response> {
    const timeoutMs = request.options.timeoutMs ?? options.timeoutMs
    const signals: AbortSignal[] = []
    if (request.options.signal) signals.push(request.options.signal)
    if (timeoutMs) signals.push(AbortSignal.timeout(timeoutMs))

    let outgoing = new Request(url, {
      method,
      body,
      // Same-origin, so the auth cookie rides along. This is why the dev proxy belongs on the
      // bundler's side: one origin in development means no CORS and no SameSite=None.
      credentials: 'same-origin',
      signal: signals.length ? AbortSignal.any(signals) : undefined,
      headers: {
        [wire.requestHeader]: wire.requestHeaderValue,
        ...extraHeaders,
        ...request.options.headers,
      },
    })

    if (options.onRequest) outgoing = await options.onRequest(outgoing)

    try {
      return await doFetch(outgoing)
    } catch (cause) {
      if (request.options.signal?.aborted) throw cause
      throw new RaskDispatchError(request.name, 0, { cause })
    }
  }

  async function fail(request: DispatchRequest, response: Response): Promise<never> {
    const contentType = response.headers.get('content-type') ?? ''
    const problem =
      contentType.includes('problem+json') || contentType.includes('application/json')
        ? readProblem(await response.text())
        : {}

    const error = new RaskDispatchError(request.name, response.status, {
      title: problem.title ?? response.statusText,
      detail: problem.detail,
      problemType: problem.type,
    })
    if (error.isUnauthorized) options.onUnauthorized?.(error)
    throw error
  }

  /**
   * Replaces every File in the payload with the integer index the server reserves for it, and hands
   * back the files in that order. The pairing of part name to index is the only thing putting a file
   * back on the property it came from — a mismatch does not fail, it quietly hands the handler
   * somebody else's file.
   */
  function extractFiles(request: DispatchRequest): { json: string; files: File[] } {
    if (request.files.length === 0) {
      return { json: JSON.stringify(request.payload), files: [] }
    }

    const payload = { ...(request.payload as Record<string, unknown>) }
    const files: File[] = []
    for (const property of request.files) {
      const value = payload[property]
      if (value instanceof File || value instanceof Blob) {
        payload[property] = files.length
        files.push(value instanceof File ? value : new File([value], property))
      }
    }
    return { json: JSON.stringify(payload), files }
  }

  async function upload(request: DispatchRequest, files: File[]): Promise<string> {
    const uploadId = crypto.randomUUID().replace(/-/g, '')

    for (let index = 0; index < files.length; index++) {
      const file = files[index]
      let offset = 0
      for (const [start, end] of planChunks(file.size, wire.uploadChunkSize)) {
        if (start < offset) continue
        const response = await call(
          request,
          'POST',
          `${base}${wire.routePrefix}/${wire.uploadSegment}`,
          file.slice(start, end),
          {
            [wire.uploadHeader]: uploadId,
            [wire.uploadFileHeader]: String(index),
            [wire.uploadOffsetHeader]: String(start),
            [wire.uploadNameHeader]: encodeURIComponent(file.name),
            [wire.uploadTypeHeader]: encodeURIComponent(file.type || 'application/octet-stream'),
          },
        )

        // The server echoes the offset it holds on success AND on a mismatch, so a resume never has
        // to guess — take its number rather than our own running count.
        const held = Number(response.headers.get(wire.uploadOffsetHeader))
        if (!response.ok && response.status !== 409) await fail(request, response)
        offset = Number.isFinite(held) ? held : end
      }

      request.options.onUploadProgress?.({
        file: index,
        fileName: file.name,
        sent: file.size,
        total: file.size,
      })
    }

    return uploadId
  }

  return {
    async send(request: DispatchRequest): Promise<unknown> {
      const { json, files } = extractFiles(request)
      const totalBytes = files.reduce((sum, file) => sum + file.size, 0)

      let response: Response
      if (files.length > 0 && totalBytes >= wire.chunkedUploadThreshold) {
        // Large files travel first, in bounded chunks; the message then spends the session. All or
        // nothing per message, because the server resolves one message's files from one source.
        const uploadId = await upload(request, files)
        response = await call(
          request,
          'POST',
          `${base}${wire.routePrefix}/${encodeURIComponent(request.name)}`,
          json,
          { 'content-type': 'application/json', [wire.uploadHeader]: uploadId },
        )
      } else if (files.length > 0) {
        const form = new FormData()
        form.append('message', new Blob([json], { type: 'application/json' }))
        // Named by the index the message reserved — the server rejects any other naming.
        files.forEach((file, index) => form.append(String(index), file, file.name))
        // Content-Type is deliberately unset: the browser owns the multipart boundary.
        response = await call(
          request,
          'POST',
          `${base}${wire.routePrefix}/${encodeURIComponent(request.name)}`,
          form,
          {},
        )
      } else if (request.kind === 'query') {
        const { method, url } = buildQueryUrl(base, request.name, json)
        response =
          method === 'GET'
            ? await call(request, 'GET', url, undefined, {})
            : await call(request, 'POST', url, json, { 'content-type': 'application/json' })
      } else {
        response = await call(
          request,
          'POST',
          `${base}${wire.routePrefix}/${encodeURIComponent(request.name)}`,
          json,
          { 'content-type': 'application/json' },
        )
      }

      if (!response.ok) await fail(request, response)

      if (request.returnsFile) {
        const length = Number(response.headers.get('content-length'))
        const download: RaskDownload = {
          fileName:
            parseContentDisposition(response.headers.get('content-disposition')) ?? request.name,
          contentType: response.headers.get('content-type') ?? 'application/octet-stream',
          size: Number.isFinite(length) ? length : null,
          blob: () => response.blob(),
          async save(fileName?: string) {
            const url = URL.createObjectURL(await response.blob())
            const anchor = document.createElement('a')
            anchor.href = url
            anchor.download = fileName ?? download.fileName
            anchor.click()
            setTimeout(() => URL.revokeObjectURL(url), 0)
          },
        }
        return download
      }

      // 204 for a void command, 202 for a notification, and an empty 200 for a null result.
      if (response.status === 204 || response.status === 202) return undefined
      const text = await response.text()
      return text.length === 0 ? undefined : JSON.parse(text)
    },
  }
}

/**
 * The call site's entry point, mirroring IDispatcher: one method, and the result type inferred from
 * the message rather than asserted at the call site.
 *
 * The single cast in the whole client is the one below, where the transport's `unknown` becomes
 * `TResult`. It is confined to this one line on purpose: everywhere else — every generated factory,
 * every call site — the types are checked, so a wrong pairing of message and result is a compile
 * error rather than something that survives to runtime.
 */
export function createDispatcher(transport: RaskTransport = httpTransport()): RaskDispatcher {
  return {
    async dispatch<TResult>(msg: Dispatchable<TResult>, options: CallOptions = {}): Promise<TResult> {
      const answer = await transport.send({
        name: msg.name,
        kind: msg.kind,
        files: msg.files,
        returnsFile: msg.returnsFile,
        payload: msg.payload,
        options,
      })

      // Revived here rather than inside the transport so a custom transport — a test double, a
      // worker bridge — gets it for free instead of having to remember to do it.
      return revive(answer, msg.result) as TResult
    },
  }
}

/** The dispatcher the generated helpers and the app use by default. */
export const rask: RaskDispatcher = createDispatcher()
