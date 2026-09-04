import { fileURLToPath } from 'node:url'
import { defineConfig } from "vite";
import { nitro } from "nitro/vite";
import { solidStart } from "@solidjs/start/config";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  resolve: {
    alias: { '@rask/': fileURLToPath(new URL('./src/rask/', import.meta.url)) }
  },
  server: {
    // In development the browser talks to this dev server, and it forwards the CQRS calls to
    // the ASP.NET host — so HMR is native, and there is no CORS to configure because the browser
    // only ever sees one origin. In production this is not used at all: Kestrel owns the port
    // and answers /_rask itself.
    proxy: {
      '/_rask': { target: 'http://localhost:5000', changeOrigin: true }
    }
  },
  plugins: [
    solidStart(),
    tailwindcss(),
    nitro()
  ]
});
