import type { NextConfig } from 'next'

const nextConfig: NextConfig = {
  // The host runs `node .next/standalone/server.js`, and this is the only output mode that
  // writes it. Without it the build succeeds and startup fails naming a path that was never
  // created.
  //
  // Standalone deliberately omits `public` and `.next/static`, assuming a CDN in front. Here
  // Kestrel IS the thing in front and serves them itself, so that omission costs nothing.
  output: 'standalone',

  // In development the browser talks to Next, and Next forwards the CQRS calls to the ASP.NET
  // host — so HMR is native and there is no CORS to configure. In production this is not used:
  // Kestrel owns the port and answers /_rask itself.
  async rewrites() {
    return [
      { source: '/_rask/:path*', destination: 'http://localhost:5000/_rask/:path*' },
    ]
  },
}

export default nextConfig
