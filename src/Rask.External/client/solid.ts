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

import { createComponent, For, mergeProps, type Component, type JSX } from 'solid-js'
import { createStore, reconcile, type SetStoreFunction } from 'solid-js/store'
import { render } from 'solid-js/web'
import type { ExternalAdapter, ExternalNode, ExternalProps } from './adapter'

/** The island's props and children, as ONE store: Solid tracks both per property, down the whole tree. */
interface SolidTree {
  props: ExternalProps
  children: readonly ExternalNode[] | null
}

/** What `mount` hands back: the disposer, and the setter updates are written through. */
interface SolidHandle {
  dispose: () => void
  setTree: SetStoreFunction<SolidTree>
}

/**
 * Wraps a Solid component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary Solid component with no Rask import in it.
 */
export function solidComponent(Component: Component<ExternalProps>): ExternalAdapter<SolidHandle> {
  return {
    mount(element, props, children) {
      // A store rather than a signal holding the props object. Solid tracks the store per PROPERTY,
      // so an update re-runs only what actually read the prop that changed — a signal would make
      // every reader of every prop re-run on any change, which is the granularity Solid exists to
      // avoid. The store is created once and lives for the island; the component is never re-created.
      const [tree, setTree] = createStore<SolidTree>({ props: { ...props }, children: children ?? null })

      // `mergeProps` keeps the store's props live — spreading them here would read every property once,
      // at mount, and freeze the values — and its `children` getter reads the store too, so a child C#
      // adds later appears without re-creating the island.
      const dispose = render(() => createComponent(Component, withChildren(tree.props, () => tree.children)), element)

      return { dispose, setTree }
    },

    update(handle, props, children) {
      // `reconcile` without `merge` removes keys the new props do not have, which is the behaviour
      // this needs: an unwired callback omits its key entirely, and merging would leave the stale one
      // in place and firing. Unchanged values keep their identity, so nothing downstream re-runs, and
      // `key: 'key'` matches child islands by their C# key, so a reordered keyed list moves them rather
      // than re-creating them; unkeyed children match by position.
      handle.setTree(reconcile({ props: { ...props }, children: children ?? null }, { key: 'key' }))
      return handle
    },

    unmount(handle) {
      handle.dispose()
    },
  }
}

/** Props with a live `children` getter, which renders nothing while the island has no children. */
function withChildren(props: ExternalProps, children: () => readonly ExternalNode[] | null): ExternalProps {
  return mergeProps(props, {
    get children(): JSX.Element {
      const list = children()
      return list === null ? undefined : renderNodes(list)
    },
  })
}

/**
 * The children as Solid nodes. `For` keys by item identity, which `reconcile` preserves for an unchanged
 * child island, so a child is created once and its props flow in through the store. Text children are
 * strings, which `For` keys by value: a changed text becomes a new text node, which holds no state to lose.
 */
function renderNodes(list: readonly ExternalNode[]): JSX.Element {
  return createComponent(For, {
    get each() {
      return list
    },
    children: (node: ExternalNode) =>
      typeof node === 'string'
        ? node
        : createComponent(node.component as Component<ExternalProps>, withChildren(node.props, () => node.children)),
  })
}
