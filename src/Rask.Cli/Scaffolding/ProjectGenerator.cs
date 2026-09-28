namespace Rask.Cli.Scaffolding;

/// <summary>
/// Generates a whole project directly (the CLI is the scaffolding authority — no <c>dotnet new</c> /
/// Rask.Templates). Each template is hand-ported here: files are emitted with the placeholder namespace
/// <c>Company.RaskServer</c> and a final pass rewrites it (and the csproj filename) to the app name, so the
/// content reads exactly like the source template. Flag conditionals (<c>--auth</c>/<c>--pwa</c>/<c>--cqrs</c>/
/// <c>--docker</c>) are generation logic, not <c>#if</c> markers. Package references are pinned to the
/// version the caller passes (the CLI's own version).
/// </summary>
/// <remarks>
/// One template per partial file — <c>.Server.cs</c>, <c>.Wasm.cs</c>, <c>.WasmHosted.cs</c>, <c>.Spa.cs</c>, <c>.Meta.cs</c>.
/// This remark is the map a reader uses to find the emitter for a template, so it has to name files that exist.
/// <para>
/// Every template is a SINGLE project. <c>wasm-hosted</c> does not scaffold a second one: it writes the
/// browser app into <c>Client/</c>, and the build generates the browser half into <c>obj/</c> from
/// <c>Client/</c> and <c>Shared/</c>. The name is back (#1103) but the shape is not — the original
/// <c>wasm-hosted</c> was a hand-written Client/Server/Shared trio with its own <c>.sln</c> and six
/// GUIDs, removed in #877 precisely because the one-project build now does that automatically.
/// </para>
/// </remarks>
internal static partial class ProjectGenerator;
