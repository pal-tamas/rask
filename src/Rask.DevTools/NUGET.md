# Rask.DevTools

**In-page developer tools** for [Rask](https://rask.sh/), a full-stack .NET web framework for teams of any
size. A panel inside the page you are building shows what the page and the app say to each other, which
components the page is made of and what each was given, which of them render and why, where each
interaction's time goes, and what went wrong.

- **No code in the app.** Referencing the package attaches the tools to the ASP.NET host and to
  WebAssembly in a Debug build. Click the **Rask** pill in the page's bottom-left corner, or press
  **Ctrl+Shift+D** (**Cmd+Shift+D** on a Mac).
- **Nothing of it ships.** A Release publish leaves out the assembly, its scripts and its `.deps.json`
  entry — then checks the output and fails the publish if any of them is still there.
- **Local by default.** The Server host shows the panel only in `Development` to a browser on the same
  machine; WebAssembly only on a loopback address.

## Install

The panel is drawn with the Rask UI kit, so the app references that too:

```bash
dotnet add package Rask.DevTools
dotnet add package Rask.Ui
```

An app made with `rask new` already has both.

Guide: [DevTools](https://rask.sh/docs/guides/devtools)
