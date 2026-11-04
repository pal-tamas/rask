// A plain custom element, used as a Lit-runtime island: no npm import, no decorators (Vite's transform does not lower
// them, and an element that never upgrades renders empty with nothing said).
//
// Its button navigates with the app's C# routes: `@rask/routes` is generated from the [Route] pages of this project,
// writes the URL under the app's path base, and hands it to the host's `__raskHost.navigate`.
import type { IslandGoProps } from '@rask/IslandGo.props'
import { Routes } from '@rask/routes'

class IslandGoElement extends HTMLElement {
  #caption: IslandGoProps['caption'] = ''

  set caption(value: string) {
    this.#caption = value
    this.#render()
  }

  connectedCallback(): void {
    this.#render()
  }

  #render(): void {
    this.replaceChildren()
    const caption = document.createElement('span')
    caption.dataset.testid = 'island-caption'
    caption.textContent = this.#caption
    const go = document.createElement('button')
    go.type = 'button'
    go.dataset.testid = 'island-go'
    go.textContent = 'Go'
    go.addEventListener('click', () => Routes.IslandArrivedPage().Go())
    this.append(caption, go)
  }
}

customElements.define('server-e2e-island-go', IslandGoElement)

export default 'server-e2e-island-go'
