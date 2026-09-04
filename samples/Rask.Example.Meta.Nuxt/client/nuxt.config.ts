import { fileURLToPath } from 'node:url'
import tailwindcss from '@tailwindcss/vite'

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  devtools: { enabled: true },

  // Tailwind through its Vite plugin, not the standalone binary: this project already has node
  // and a bundler, and the plugin keeps the bundler's own hot reload for CSS. Nuxt's creator has
  // no option for this, so Rask wires it — the other frameworks on this lane are asked for
  // Tailwind by their own creators instead.
  css: ['~/assets/css/main.css'],
  vite: { plugins: [tailwindcss()] },

  // `@rask/browser/geolocation`, `@rask/client`, `@rask/messages` — the browser layer and the
  // TypeScript projection of your C# message records, written into app/rask/ on every build.
  //
  // Declared here rather than in tsconfig.json, which is the one thing about this lane that is
  // Nuxt-specific: Nuxt GENERATES its tsconfigs into .nuxt/ and the root one only references
  // them, so an `extends` written there is not in the program that type-checks the app. Nuxt
  // propagates these aliases into the config it writes, which is why this is the way in.
  alias: {
    '@rask': fileURLToPath(new URL('./app/rask', import.meta.url)),
  },

  nitro: {
    // The host runs `node .output/server/index.mjs`, so the preset has to be the node one. It is
    // already Nitro's default; named because a preset changed for some other deploy target would
    // otherwise break STARTUP rather than the build, and much later.
    preset: 'node-server',

    // In development the browser talks to Nuxt, and Nuxt forwards the CQRS calls to the ASP.NET
    // host — so HMR is native and there is no CORS to configure, because the browser only ever
    // sees one origin. In production this is not used at all: Kestrel owns the port and answers
    // /_rask itself.
    devProxy: {
      '/_rask': { target: 'http://localhost:5000/_rask', changeOrigin: true },
    },
  },
})
