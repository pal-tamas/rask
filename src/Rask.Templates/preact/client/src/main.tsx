import { render } from 'preact'
import { App } from './app'
import { Auth } from './auth'
import './index.css'

// No router, deliberately — see the note in the React template. The path is read once, and
// deep links work because the dev server and the host both fall back to index.html.
const path = window.location.pathname
const root = document.getElementById('app')!

if (path === '/login') render(<Auth mode="login" />, root)
else if (path === '/register') render(<Auth mode="register" />, root)
else render(<App />, root)
