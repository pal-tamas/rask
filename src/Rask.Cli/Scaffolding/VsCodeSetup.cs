namespace Rask.Cli.Scaffolding;

/// <summary>Which VS Code debugging setup a template ships.</summary>
internal enum VsCodeSetup
{
    /// <summary>No <c>.vscode/</c> at all.</summary>
    None,

    /// <summary>An ASP.NET host: F5 runs it under the C# debugger.</summary>
    Host,

    /// <summary>
    ///     An ASP.NET host with a Rask WebAssembly client (the <c>wasm-hosted</c> template): the host under the C# debugger,
    ///     then the client in a browser under the JavaScript debugger, through the host's debug proxy.
    /// </summary>
    WasmHost,

    /// <summary>A standalone browser-WASM app: its dev server in the background, the app in a debugged browser.</summary>
    WasmBrowser,
}
