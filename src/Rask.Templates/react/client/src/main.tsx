import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import Auth from './Auth'
import './index.css'

// No router, deliberately. Which one to reach for is a decision a front-end developer has
// usually already made, and scaffolding one would make it for them — so this reads the path
// once, and the day you add a router it is three lines to delete. Deep links work already:
// the dev server and the host both fall back to index.html for an unknown path.
const path = window.location.pathname

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {path === '/login' ? (
      <Auth mode="login" />
    ) : path === '/register' ? (
      <Auth mode="register" />
    ) : (
      <App />
    )}
  </StrictMode>,
)
