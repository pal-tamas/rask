// rask-external Angular adapter, vendored from Rask.External.
//
// The only runtime here whose bootstrap is ASYNCHRONOUS. `createApplication()` returns a promise, so
// between mount() and the component existing there is a window in which props can arrive and the
// island can be removed again — both are handled below, because neither is rare: Rask re-renders as
// soon as state changes, and a `hydrate="visible"` island can leave the viewport while booting.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createComponent, reflectComponentType } from '@angular/core'
import type { ApplicationRef, ComponentRef, Type } from '@angular/core'
import { createApplication } from '@angular/platform-browser'
import type { ExternalAdapter, ExternalNode, ExternalProps } from './adapter'

/** One output a handler is subscribed to, kept so an update can tell whether the handler changed. */
interface BoundOutput {
  handler: unknown
  subscription: { unsubscribe(): void }
}

/** One component this adapter created — the island's own, or a child island — and everything it was given. */
interface AngularView {
  ref: ComponentRef<unknown>
  component: Type<unknown>
  host: HTMLElement
  outputs: Map<string, BoundOutput>
  /** Each input's value before this adapter first set it, keyed by alias, to restore when C# stops sending it. */
  originals: Map<string, unknown>
  /**
   * Where its children are placed: one `display: contents` element projected into its default `<ng-content>`.
   * Projected nodes are fixed when a component is created, so the children are reconciled INSIDE this element
   * rather than handed to Angular again on every update.
   */
  container: HTMLElement
  /** What this adapter placed in `container`, in order. */
  placed: Placed[]
}

type Placed = { text: Text } | { key: string | null; view: AngularView }

/**
 * What `mount` hands back immediately, before Angular has booted.
 *
 * `app` and `view` are optional because the handle exists before the application does. Rask hands it
 * back to `update` and `unmount` either way.
 */
interface AngularHandle {
  app?: ApplicationRef
  view?: AngularView
  node: HTMLElement
  props: ExternalProps
  children: readonly ExternalNode[] | undefined
  disposed: boolean
}

/**
 * Wraps a standalone Angular component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary Angular component with no Rask import in it.
 */
export function angularComponent(component: Type<unknown>): ExternalAdapter<AngularHandle> {
  return {
    mount(element, props, children) {
      // Created into a child this adapter OWNS, not into Rask's host element — the same shape the Lit
      // adapter uses, and here it is load-bearing rather than tidy. Angular treats whatever it is given
      // as the component's root node, and destroying a view does not remove its own root: measured,
      // `app.destroy()` on a component bootstrapped straight into the host left the whole rendered tree
      // sitting in the DOM. With a child, teardown is `node.remove()` and nothing is left behind.
      const node = document.createElement('div')
      element.appendChild(node)

      const handle: AngularHandle = { node, props: { ...props }, children, disposed: false }

      void createApplication()
        .then((app) => {
          // Unmounted while booting. Destroying the application we were just handed is the whole
          // point of the flag: dropping it instead would leave Angular's change detection running
          // against an element that is no longer in the document, for the life of the page.
          if (handle.disposed) {
            app.destroy()
            return
          }

          handle.app = app
          handle.view = createView(app, component, handle.node)

          // `handle.props` and `handle.children`, not the ones this closure was created with: an update
          // may have landed while the promise was in flight, and applying the mount-time values would
          // render the island one state behind with nothing to correct it.
          apply(app, handle.view, handle.props, handle.children)
        })
        .catch((error: unknown) => {
          console.error('[rask-external] Angular island failed to bootstrap', error)
        })

      return handle
    },

    update(handle, props, children) {
      handle.props = { ...props }
      handle.children = children

      if (handle.app && handle.view) {
        apply(handle.app, handle.view, handle.props, handle.children)
      }

      return handle
    },

    unmount(handle) {
      handle.disposed = true
      if (handle.view) {
        destroyView(handle.view)
      }

      handle.app?.destroy()
      handle.node.remove()
    },
  }
}

/**
 * Creates a component into `host` and attaches it to the application's change detection.
 *
 * `createComponent` rather than `app.bootstrap`: it is the documented way to create a component outside a
 * template, and the only one that takes `projectableNodes` — which is how children reach `<ng-content>`.
 */
function createView(app: ApplicationRef, component: Type<unknown>, host: HTMLElement): AngularView {
  const container = document.createElement('div')
  container.style.display = 'contents'

  const ref = createComponent(component, {
    environmentInjector: app.injector,
    hostElement: host,
    projectableNodes: [[container]],
  })
  app.attachView(ref.hostView)

  return { ref, component, host, outputs: new Map(), originals: new Map(), container, placed: [] }
}

/** Unsubscribes, destroys the children this view placed, then the view itself. */
function destroyView(view: AngularView): void {
  for (const bound of view.outputs.values()) {
    bound.subscription.unsubscribe()
  }

  view.outputs.clear()
  for (const placed of view.placed) {
    if ('view' in placed) {
      destroyView(placed.view)
    }
  }

  view.placed = []
  view.ref.destroy()
  view.host.remove()
}

/**
 * Writes props as component INPUTS, binds a prop named `@alias` to the output of that name, and
 * reconciles the children.
 *
 * `setInput` is the only route that marks the view dirty, so assigning to the instance directly would
 * update the field and never repaint. It also skips a value that is unchanged by `Object.is`, which
 * is what keeps a re-render from invalidating the whole island — and why the runtime's handler cache
 * keeping callback identity matters here too. An input is set by its PUBLIC name, the alias, which is
 * the name the build sends it under.
 *
 * An output is not an input and cannot be set: it is subscribed to. The build names such a prop
 * `@valueChange`; the component's own metadata says which property carries that output.
 *
 * A prop that is not declared `@Input()` (or `input()`) cannot be set: Angular reports it in a
 * development build and ignores it silently in a production one.
 */
function apply(app: ApplicationRef, view: AngularView, props: ExternalProps, children: readonly ExternalNode[] | undefined): void {
  const mirror = reflectComponentType(view.component)
  const outputs = mirror?.outputs ?? []
  const inputs = mirror?.inputs ?? []

  for (const key of Object.keys(props)) {
    if (key.startsWith('@')) {
      bindOutput(view, outputs, key.slice(1), props[key])
      continue
    }

    if (!view.originals.has(key)) {
      view.originals.set(key, currentInput(view.ref, inputs, key))
    }

    view.ref.setInput(key, props[key])
  }

  for (const [alias, bound] of [...view.outputs]) {
    if (!Object.prototype.hasOwnProperty.call(props, `@${alias}`)) {
      bound.subscription.unsubscribe()
      view.outputs.delete(alias)
    }
  }

  // C# leaves an unset prop out rather than sending null, so an input that is no longer sent goes back to the value the
  // component held before Rask first set it — its own default, as the other runtimes get by re-rendering from all props.
  for (const [alias, original] of [...view.originals]) {
    if (!Object.prototype.hasOwnProperty.call(props, alias)) {
      view.ref.setInput(alias, original)
      view.originals.delete(alias)
    }
  }

  renderChildren(app, view, children)

  // Explicit, because the island is driven from outside Angular: the props were written by Rask's
  // runtime, not by an Angular event handler, so nothing has scheduled a tick for them.
  view.ref.changeDetectorRef.detectChanges()
}

/**
 * Places a view's children in its projected container: text as text nodes, a child island as its own component in its
 * own host element, matched by C# key — or, unkeyed, by component in order — so an unchanged child keeps its instance
 * and state. A node is moved only when it is out of place.
 */
function renderChildren(app: ApplicationRef, view: AngularView, children: readonly ExternalNode[] | undefined): void {
  const previous = view.placed
  if (previous.length === 0 && !children?.length) {
    return
  }

  const keyed = new Map<string, { key: string | null; view: AngularView }>()
  const unkeyed = new Map<Type<unknown>, { key: string | null; view: AngularView }[]>()
  const texts: { text: Text }[] = []
  for (const placed of previous) {
    if ('text' in placed) {
      texts.push(placed)
    } else if (placed.key !== null) {
      keyed.set(placed.key, placed)
    } else {
      const queue = unkeyed.get(placed.view.component) ?? []
      queue.push(placed)
      unkeyed.set(placed.view.component, queue)
    }
  }

  const next: Placed[] = []
  for (const child of children ?? []) {
    if (typeof child === 'string') {
      const placed = texts.shift() ?? { text: document.createTextNode('') }
      if (placed.text.data !== child) {
        placed.text.data = child
      }

      next.push(placed)
      continue
    }

    const component = child.component as Type<unknown>
    let match = child.key !== null ? keyed.get(child.key) : unkeyed.get(component)?.shift()
    if (match && match.view.component !== component) {
      match = undefined
    }

    if (match && child.key !== null) {
      keyed.delete(child.key)
    }

    const placed = match ?? { key: child.key, view: createView(app, component, document.createElement('div')) }
    apply(app, placed.view, child.props, child.children ?? undefined)
    next.push(placed)
  }

  const kept = new Set(next)
  for (const placed of previous) {
    if (!kept.has(placed)) {
      if ('view' in placed) {
        destroyView(placed.view)
      } else {
        placed.text.remove()
      }
    }
  }

  let before: Node | null = null
  for (let i = next.length - 1; i >= 0; i--) {
    const placed = next[i]
    const node = 'text' in placed ? placed.text : placed.view.host
    if (node.parentNode !== view.container || node.nextSibling !== before) {
      view.container.insertBefore(node, before)
    }

    before = node
  }

  view.placed = next
}

/** The value the input published as `alias` holds now — a signal input's value, not the signal. */
function currentInput(
  ref: ComponentRef<unknown>,
  inputs: ReadonlyArray<{ readonly propName: string; readonly templateName: string; readonly isSignal?: boolean }>,
  alias: string,
): unknown {
  const input = inputs.find((i) => i.templateName === alias)
  if (!input) {
    return undefined
  }

  const value = (ref.instance as Record<string, unknown>)[input.propName]
  return input.isSignal && typeof value === 'function' ? (value as () => unknown)() : value
}

/** Subscribes `handler` to the output published as `alias`, replacing a different handler bound before it. */
function bindOutput(
  view: AngularView,
  outputs: ReadonlyArray<{ readonly propName: string; readonly templateName: string }>,
  alias: string,
  handler: unknown,
): void {
  const previous = view.outputs.get(alias)
  if (previous?.handler === handler) {
    return
  }

  previous?.subscription.unsubscribe()
  view.outputs.delete(alias)

  const output = outputs.find((o) => o.templateName === alias)
  const emitter = output
    ? ((view.ref.instance as Record<string, unknown>)[output.propName] as
        | { subscribe?(next: (value: unknown) => void): { unsubscribe(): void } }
        | undefined)
    : undefined

  if (typeof handler !== 'function' || typeof emitter?.subscribe !== 'function') {
    return
  }

  view.outputs.set(alias, {
    handler,
    subscription: emitter.subscribe((value) => (handler as (value: unknown) => void)(value)),
  })
}
