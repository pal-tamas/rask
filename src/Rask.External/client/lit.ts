// rask-external Lit adapter, vendored from Rask.External.
//
// The cheapest runtime of the set, and the only one that imports nothing: a Lit component IS a custom
// element, so mounting is createElement plus property assignment, updating is the same assignment,
// and unmounting is remove(). Lit's reactive properties re-render on assignment with nothing in
// between — there is no reconciler to drive.
//
// This works for any custom element with property-shaped inputs, not only Lit ones.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import type { ExternalAdapter, ExternalNode, ExternalProps } from './adapter'

/** The listeners each element was given, so an update swaps a changed handler and an unmount removes them all. */
const listeners = new WeakMap<HTMLElement, Map<string, EventListener>>()

/** The value each property had before this adapter first wrote it, so a prop C# stops sending can fall back to it. */
const originals = new WeakMap<HTMLElement, Map<string, unknown>>()

/** The nodes this adapter placed inside each element, in order — never content the element put there itself. */
const placed = new WeakMap<HTMLElement, Node[]>()

/** The C# key each child element was placed under, or null for an unkeyed one. */
const keys = new WeakMap<HTMLElement, string | null>()

/**
 * Wraps a custom element as an island adapter.
 *
 * @param tag The registered element name, e.g. `'app-gauge'`. Importing the module that defines it is
 *   the build's job — the generated entry imports the component for its side effect and calls this.
 */
export function litComponent(tag: string): ExternalAdapter<HTMLElement> {
  return {
    mount(element, props, children) {
      const node = document.createElement(tag)
      assign(node, props)
      renderChildren(node, children)
      element.appendChild(node)
      return node
    },

    update(node, props, children) {
      assign(node, props)
      renderChildren(node, children)
      return node
    },

    unmount(node) {
      release(node)
      node.remove()
    },
  }
}

/**
 * Assigns props as PROPERTIES, never attributes — except a prop named `@event`, which is a listener.
 *
 * An attribute would stringify everything — an array of points would arrive as "[object Object]" —
 * and Lit only reflects the direction it was asked to. Properties also carry the revived callbacks,
 * which cannot survive an attribute at all.
 *
 * An element's events are not properties: `sl-change` is dispatched, and a handler has to be listening
 * for it. The build names such a prop `@sl-change`, so it is added as a listener instead, swapped only
 * when the handler itself changed — the runtime keeps a callback's identity across renders, so an
 * unchanged one stays attached — and removed when the prop is gone.
 */
function assign(node: HTMLElement, props: ExternalProps): void {
  const bound = listeners.get(node) ?? new Map<string, EventListener>()
  listeners.set(node, bound)
  const written = originals.get(node) ?? new Map<string, unknown>()
  originals.set(node, written)
  const fields = node as unknown as Record<string, unknown>

  for (const key of Object.keys(props)) {
    if (!key.startsWith('@')) {
      if (!written.has(key)) {
        written.set(key, fields[key])
      }

      fields[key] = props[key]
      continue
    }

    const name = key.slice(1)
    const handler = props[key]
    const previous = bound.get(name)
    if (previous === handler) {
      continue
    }

    if (previous) {
      node.removeEventListener(name, previous)
      bound.delete(name)
    }

    if (typeof handler === 'function') {
      node.addEventListener(name, handler as EventListener)
      bound.set(name, handler as EventListener)
    }
  }

  for (const [name, handler] of [...bound]) {
    if (!Object.prototype.hasOwnProperty.call(props, `@${name}`)) {
      node.removeEventListener(name, handler)
      bound.delete(name)
    }
  }

  // C# leaves an unset prop out rather than sending null, so a prop that is no longer sent has to be undone here: it
  // goes back to what the element held before Rask first set it — the same fallback to the component's own default
  // that React, Vue and Svelte get by rendering from the whole props object.
  for (const [key, original] of [...written]) {
    if (!Object.prototype.hasOwnProperty.call(props, key)) {
      fields[key] = original
      written.delete(key)
    }
  }
}

/**
 * Places an element's children in its light DOM, where a Lit element's `<slot>` projects them.
 *
 * Reconciled rather than rebuilt: a child element is matched by its C# key, or — unkeyed — by tag in order, and keeps
 * its identity and so its state; text nodes are reused in order. A node is moved only when it is out of place, because
 * moving a custom element disconnects and reconnects it, which re-runs its connected lifecycle. Only nodes this adapter
 * placed are ever touched.
 */
function renderChildren(parent: HTMLElement, children: readonly ExternalNode[] | null | undefined): void {
  const previous = placed.get(parent) ?? []
  if (previous.length === 0 && !children?.length) {
    return
  }

  const keyed = new Map<string, HTMLElement>()
  const unkeyed = new Map<string, HTMLElement[]>()
  const texts: Text[] = []
  for (const node of previous) {
    if (node.nodeType === Node.TEXT_NODE) {
      texts.push(node as Text)
      continue
    }

    const element = node as HTMLElement
    const key = keys.get(element) ?? null
    if (key !== null) {
      keyed.set(key, element)
    } else {
      const queue = unkeyed.get(element.localName) ?? []
      queue.push(element)
      unkeyed.set(element.localName, queue)
    }
  }

  const next: Node[] = []
  for (const child of children ?? []) {
    if (typeof child === 'string') {
      const text = texts.shift() ?? document.createTextNode('')
      if (text.data !== child) {
        text.data = child
      }

      next.push(text)
      continue
    }

    const tag = child.component as string
    let element = child.key !== null ? keyed.get(child.key) : unkeyed.get(tag)?.shift()
    if (element && element.localName !== tag) {
      element = undefined
    }

    if (element && child.key !== null) {
      keyed.delete(child.key)
    }

    element ??= document.createElement(tag)
    keys.set(element, child.key)
    assign(element, child.props)
    renderChildren(element, child.children)
    next.push(element)
  }

  const kept = new Set(next)
  for (const node of previous) {
    if (!kept.has(node)) {
      if (node.nodeType === Node.ELEMENT_NODE) {
        release(node as HTMLElement)
      }

      node.parentNode?.removeChild(node)
    }
  }

  // From the last child backwards, each placed right before the one after it: an already-placed node stays put.
  let before: Node | null = null
  for (let i = next.length - 1; i >= 0; i--) {
    const node = next[i]
    if (node.parentNode !== parent || node.nextSibling !== before) {
      parent.insertBefore(node, before)
    }

    before = node
  }

  placed.set(parent, next)
}

/** Removes everything this adapter attached to an element and to the children it placed inside it. */
function release(node: HTMLElement): void {
  for (const [name, handler] of listeners.get(node) ?? []) {
    node.removeEventListener(name, handler)
  }

  for (const child of placed.get(node) ?? []) {
    if (child.nodeType === Node.ELEMENT_NODE) {
      release(child as HTMLElement)
    }
  }

  listeners.delete(node)
  originals.delete(node)
  placed.delete(node)
  keys.delete(node)
}
