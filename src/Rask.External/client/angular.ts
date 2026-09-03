// rask-external Angular adapter, vendored from Rask.External.
//
// The only runtime here whose bootstrap is ASYNCHRONOUS. `createApplication()` returns a promise, so
// between mount() and the component existing there is a window in which props can arrive and the
// island can be removed again — both are handled below, because neither is rare: Rask re-renders as
// soon as state changes, and a `hydrate="visible"` island can leave the viewport while booting.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import type { ApplicationRef, ComponentRef, Type } from '@angular/core'
import { createApplication } from '@angular/platform-browser'
import type { ExternalAdapter, ExternalProps } from './adapter'

/**
 * What `mount` hands back immediately, before Angular has booted.
 *
 * Everything but `node`, `props` and `disposed` is optional, because the handle exists before the
 * application does. Rask hands it back to `update` and `unmount` either way.
 */
interface AngularHandle {
  app?: ApplicationRef
  component?: ComponentRef<unknown>
  node: HTMLElement
  props: ExternalProps
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
    mount(element, props) {
      // Bootstrapped into a child this adapter OWNS, not into Rask's host element — the same shape
      // the Lit adapter uses, and here it is load-bearing rather than tidy. Angular treats whatever
      // it is given as the component's root node, and destroying a view does not remove its own root:
      // measured, `app.destroy()` on a component bootstrapped straight into the host left the whole
      // rendered tree sitting in the DOM, and `componentRef.destroy()` first made no difference. With
      // a child, teardown is `node.remove()` and nothing is left behind.
      const node = document.createElement('div')
      element.appendChild(node)

      const handle: AngularHandle = { node, props: { ...props }, disposed: false }

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

          const ref = app.bootstrap(component, handle.node)
          handle.component = ref

          // `handle.props`, not the `props` this closure was created with: an update may have landed
          // while the promise was in flight, and applying the mount-time values would render the
          // island one state behind with nothing to correct it.
          apply(ref, handle.props)
        })
        .catch((error: unknown) => {
          console.error('[rask-external] Angular island failed to bootstrap', error)
        })

      return handle
    },

    update(handle, props) {
      handle.props = { ...props }

      if (handle.component) {
        apply(handle.component, handle.props)
      }

      return handle
    },

    unmount(handle) {
      handle.disposed = true
      handle.app?.destroy()
      handle.node.remove()
    },
  }
}

/**
 * Writes props as component INPUTS.
 *
 * `setInput` is the only route that marks the view dirty, so assigning to the instance directly would
 * update the field and never repaint. It also skips a value that is unchanged by `Object.is`, which
 * is what keeps a re-render from invalidating the whole island — and why the runtime's handler cache
 * keeping callback identity matters here too.
 *
 * A prop that is not declared `@Input()` (or `input()`) cannot be set: Angular reports it in a
 * development build and ignores it silently in a production one.
 */
function apply(ref: ComponentRef<unknown>, props: ExternalProps): void {
  for (const key of Object.keys(props)) {
    ref.setInput(key, props[key])
  }

  // Explicit, because the island is driven from outside Angular: the props were written by Rask's
  // runtime, not by an Angular event handler, so nothing has scheduled a tick for them.
  ref.changeDetectorRef.detectChanges()
}
