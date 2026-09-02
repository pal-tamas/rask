// rask-external Solid adapter, vendored from Rask.External.
//
// A plain `.ts`, and deliberately free of JSX — `createComponent` is exactly what Solid's compiler
// emits for `<Component {...props} />`, so writing it by hand is what lets this file live outside the
// island's directory and still be correct. That matters here more than anywhere: when two JSX
// runtimes share a project their Vite plugins are scoped to their own island directories, and a JSX
// adapter sitting in obj/ would match neither scope and be compiled by whichever plugin claimed the
// rest of the tree.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createComponent, type Component } from 'solid-js'
import { createStore, reconcile, type SetStoreFunction } from 'solid-js/store'
import { render } from 'solid-js/web'
import type { ExternalAdapter, ExternalProps } from './adapter'

/** What `mount` hands back: the disposer, and the setter updates are written through. */
interface SolidHandle {
  dispose: () => void
  setProps: SetStoreFunction<ExternalProps>
}

/**
 * Wraps a Solid component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary Solid component with no Rask import in it.
 */
export function solidComponent(Component: Component<ExternalProps>): ExternalAdapter<SolidHandle> {
  return {
    mount(element, props) {
      // A store rather than a signal holding the props object. Solid tracks the store per PROPERTY,
      // so an update re-runs only what actually read the prop that changed — a signal would make
      // every reader of every prop re-run on any change, which is the granularity Solid exists to
      // avoid. The store is created once and lives for the island; the component is never re-created.
      const [store, setProps] = createStore<ExternalProps>({ ...props })

      // `createComponent(C, store)` passes the store ITSELF as props. Spreading it here would read
      // every property once, at mount, and freeze the values — the component would render correctly
      // and then never see another update.
      const dispose = render(() => createComponent(Component, store), element)

      return { dispose, setProps }
    },

    update(handle, props) {
      // `reconcile` without `merge` removes keys the new props do not have, which is the behaviour
      // this needs: an unwired callback omits its key entirely, and merging would leave the stale one
      // in place and firing. Unchanged values keep their identity, so nothing downstream re-runs.
      handle.setProps(reconcile({ ...props }))
      return handle
    },

    unmount(handle) {
      handle.dispose()
    },
  }
}
