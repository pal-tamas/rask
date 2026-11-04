// A plain custom element, used as a Lit-runtime island: no npm import beyond the app's own routes, no decorators
// (Vite's transform does not lower them, and an element that never upgrades renders empty with nothing said).
//
// The button navigates with the module the build generates from the C# [Route] pages: no path is written here, and
// under a sub-path the URL it goes to is the one C# formats under the app's base.
import { Routes } from '@rask/routes'
import type { DeskLinkProps } from '@rask/DeskLink.props'

class DeskLinkElement extends HTMLElement {
  #label: DeskLinkProps['label'] = ''

  set label(value: string) {
    this.#label = value
    this.#render()
  }

  connectedCallback(): void {
    this.#render()
  }

  #render(): void {
    const button = document.createElement('button')
    button.id = 'to-desk'
    button.type = 'button'
    button.textContent = this.#label
    button.addEventListener('click', () => Routes.DeskPage().Go())
    this.replaceChildren(button)
  }
}

customElements.define('wasm-fixture-desk-link', DeskLinkElement)

export default 'wasm-fixture-desk-link'
