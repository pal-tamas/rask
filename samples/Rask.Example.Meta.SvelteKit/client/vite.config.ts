import tailwindcss from '@tailwindcss/vite';
import adapter from '@sveltejs/adapter-node';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
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
		tailwindcss(),
		sveltekit({
			alias: { '@rask': 'src/rask' },
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) => filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			adapter: adapter()
		})
	]
});
