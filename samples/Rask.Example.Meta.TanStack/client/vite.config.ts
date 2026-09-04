import { defineConfig } from 'vite'
import { devtools } from '@tanstack/devtools-vite'

import { tanstackStart } from '@tanstack/react-start/plugin/vite'

import viteReact from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { nitro } from 'nitro/vite'

const config = defineConfig({
  server: {
    // In development the browser talks to this dev server, and it forwards the CQRS calls to
    // the ASP.NET host — so HMR is native, and there is no CORS to configure because the browser
    // only ever sees one origin. In production this is not used at all: Kestrel owns the port
    // and answers /_rask itself.
    proxy: {
      '/_rask': { target: 'http://localhost:5000', changeOrigin: true }
    }
  },
  resolve: { tsconfigPaths: true },
  plugins: [
    devtools(),
    nitro({ rollupConfig: { external: [/^@sentry\//] } }),
    tailwindcss(),
    tanstackStart(),
    viteReact(),
  ],
})

export default config
