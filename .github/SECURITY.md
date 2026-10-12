# Security Policy

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, use **[GitHub private vulnerability reporting](https://github.com/pal-tamas/rask/security/advisories/new)**
(Security → Advisories → "Report a vulnerability"). If you can't use that, email
**pal.tamas@pptgateway.hu** with details.

Please include:

- the affected component/host (Server / WASM / hosted) and version,
- a description and impact assessment,
- a minimal reproduction if possible.

You'll get an acknowledgement as soon as possible. Once a fix is ready, a patched release is
published and the advisory is disclosed with credit (unless you prefer to stay anonymous).

## Supported versions

Rask is pre-1.0; security fixes target the **latest released version** on nuget.org. Please
upgrade to the latest version before reporting.

## What an error shows a visitor

Outside Development the framework writes nothing of an exception to a browser: no type, no message, no inner
exception, no stack. The built-in error page shows a random reference, and the Error log line on `Rask.Errors`
carries the same reference beside the exception. An unknown environment is treated as Production. An app's own
`ErrorBoundary.Fallback` receives the exception and must not print it. See
[docs/lifecycle.md](../docs/lifecycle.md#what-a-visitor-sees-when-something-throws).

A handler that throws outside any boundary of the app's own keeps its page and raises a toast with that
reference; the toast and the frame that carries it hold nothing of the exception outside Development.

## Switches that work in Development only

Two settings would hand an operator's view to a caller, and both are configuration — reachable from an
environment variable. Neither takes effect outside the Development environment, whatever it is set to; a
host that sets one elsewhere logs a warning at startup saying it was ignored, and a host that names no
environment is Production.

- `Rask:Cqrs:Server:IncludeExceptionDetail` — the exception in a remote dispatch 500. Outside Development the
  body is the generic problem with a reference, and the exception is logged at Error on `Rask.Errors`.
- `Rask:Ops:AllowAnonymousAccess` — the operator dashboard at `/_rask` with no authorization. Outside
  Development the dashboard always requires the `RaskDashboard` policy and denies everyone when the app
  defines none. The dashboard is an administrator's tool; nothing in configuration opens it.
