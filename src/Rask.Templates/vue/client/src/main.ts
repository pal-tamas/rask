import { createApp, h } from 'vue'
import './style.css'
import App from './App.vue'
import Auth from './Auth.vue'

// No router, deliberately — see the note in the React template. The path is read once, and
// deep links work because the dev server and the host both fall back to index.html.
const path = window.location.pathname
const root =
  path === '/login' || path === '/register'
    ? h(Auth, { mode: path === '/register' ? 'register' : 'login' })
    : h(App)

createApp(root).mount('#app')
