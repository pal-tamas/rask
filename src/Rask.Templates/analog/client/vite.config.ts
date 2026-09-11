import { fileURLToPath } from 'node:url'
/// <reference types="vitest" />

import { defineConfig } from 'vite'
import analog from '@analogjs/platform'

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => ({
  server: {
    // In development the browser talks to this dev server, and it forwards the CQRS calls to
    // the ASP.NET host — so HMR is native, and there is no CORS to configure because the browser
    // only ever sees one origin. In production this is not used at all: Kestrel owns the port
    // and answers /_rask itself.
    proxy: {
      '/_rask': { target: 'http://localhost:5000', changeOrigin: true },
      // The accounts endpoints, which sit at /api/auth rather than under /_rask.
      '/api/auth': { target: 'http://localhost:5000', changeOrigin: true },
    },
  },
  build: {
    target: ['es2020'],
  },
  resolve: {
    alias: { '@rask/': fileURLToPath(new URL('./src/rask/', import.meta.url)) },
    mainFields: ['module'],
  },
  plugins: [analog()],
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['src/test-setup.ts'],
    include: ['**/*.spec.ts'],
    reporters: ['default'],
  },
  define: {
    'import.meta.vitest': mode !== 'production',
  },
}))
