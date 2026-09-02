// rask-external adapter contract, vendored from Rask.External.
//
// Everything Rask needs from React, Lit, Vue or Svelte fits in three functions. That is what makes the
// second and third runtimes cheap once the first exists — and why rask-external.js imports no
// framework: an island's built chunk default-exports its own adapter, so adding a runtime never
// touches the runtime.
//
// You own this file. It is refreshed on build only while the header line above is intact.

/** The props the server rendered, with every callback already a real function. */
export type ExternalProps = Record<string, unknown>

/**
 * One runtime's binding, over whatever handle that runtime uses to represent a mounted component.
 *
 * `THandle` is opaque to Rask: a React root, a Lit element, a Vue app. It is handed back to `update`
 * and `unmount` unchanged.
 */
export interface ExternalAdapter<THandle = unknown> {
  /**
   * Takes ownership of `element` and renders into it.
   *
   * Everything below `element` belongs to this runtime from here on — Rask's live diff treats the
   * subtree as opaque and will never patch inside it. An island is a LEAF on Rask's side: it is
   * handed props and nothing else, because content Rask rendered could not survive being relocated
   * by another framework — the diff addresses DOM nodes by position from the document, and every
   * path it holds into those nodes would be wrong the moment they moved.
   */
  mount(element: Element, props: ExternalProps): THandle

  /**
   * Applies new props to an already-mounted island.
   *
   * Called when C# re-rendered and the props changed — never for a DOM change, because there are no
   * DOM changes to see: Rask emits a single attribute op for the whole island and nothing else.
   * Return a new handle to replace the old one, or nothing to keep it.
   */
  update?(handle: THandle, props: ExternalProps): THandle | void

  /** Releases the component. The element is being removed either way, so this must not throw. */
  unmount?(handle: THandle): void | Promise<void>
}
