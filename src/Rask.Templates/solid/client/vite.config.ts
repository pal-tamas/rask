import solid from 'vite-plugin-solid'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [solid(), tailwindcss()],
  server: {
    // In development the browser talks to Vite, and Vite forwards the CQRS calls to the ASP.NET
    // host — so HMR is native and instant, and there is no CORS to configure because the browser
    // only ever sees one origin. In production this proxy is not used at all: the host serves the
    // built bundle and answers /_rask itself.
    proxy: {
      '/_rask': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      // The accounts endpoints, which sit at /api/auth rather than under /_rask. A second entry
      // rather than a wider pattern: this forwards what the host actually answers and leaves the
      // rest of /api to the front end, which may well want routes of its own there.
      '/api/auth': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})
