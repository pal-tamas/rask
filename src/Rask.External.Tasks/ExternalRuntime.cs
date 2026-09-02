using System;
using System.Collections.Generic;
using System.Linq;

namespace Rask.External.Tasks;

/// <summary>
///     One island runtime, as the bundler needs to see it.
/// </summary>
/// <remarks>
///     <para>
///         A table rather than a branch. The first two runtimes fit in an <c>if (lit) … else react</c>,
///         and that shape does not survive the third: every place that asked "is it Lit?" has to become
///         "which of four?", and the one that is missed does not fail — it silently generates a React
///         entry for a Vue component, which builds, ships, loads, and mounts nothing.
///     </para>
///     <para>
///         Modelled on <c>SpaFramework</c> in the CLI, which solved the same problem for the SPA lane:
///         one list, and everything derived from it.
///     </para>
/// </remarks>
internal sealed class ExternalRuntime
{
    private ExternalRuntime(
        string key,
        string importName,
        string adapterFactory,
        string? pluginImport = null,
        string? pluginCall = null,
        string? adapterModule = null)
    {
        Key = key;
        ImportName = importName;
        AdapterFactory = adapterFactory;
        PluginImport = pluginImport;
        PluginCall = pluginCall;
        AdapterModule = adapterModule ?? key;
    }

    /// <summary>The wire value: what a component's <c>Runtime</c> returns and MSBuild carries.</summary>
    public string Key { get; }

    /// <summary>
    ///     The adapter's module name under the vendored adapter directory, without its extension.
    /// </summary>
    /// <remarks>
    ///     The runtime key, except where the adapter itself has to be compiled by its framework.
    ///     Svelte's is <c>svelte.svelte</c> — a <c>.svelte.ts</c> module — because keeping props
    ///     reactive needs a <c>$state</c> proxy, and runes are a COMPILER feature: they do not exist
    ///     in a plain <c>.ts</c> file. Without it the only way to show new props is to remount, which
    ///     would throw away the component's own state on every C# re-render.
    /// </remarks>
    public string AdapterModule { get; }

    /// <summary>
    ///     The identifier the generated entry binds the author's default export to.
    /// </summary>
    /// <remarks>
    ///     Names what the export actually IS, because that is the only thing distinguishing the
    ///     runtimes here: a Lit module default-exports its registered <em>tag name</em> — a string —
    ///     while every other runtime default-exports a component.
    /// </remarks>
    public string ImportName { get; }

    /// <summary>The adapter factory the entry wraps it with, exported from <c>client/{Key}.ts</c>.</summary>
    public string AdapterFactory { get; }

    /// <summary>
    ///     The Vite plugin's import clause, or null for a runtime that needs no plugin.
    /// </summary>
    /// <remarks>
    ///     A clause rather than a package name, because the shape differs and getting it wrong is a
    ///     build error in generated code: Svelte's plugin is a NAMED export
    ///     (<c>{ svelte } from '@sveltejs/vite-plugin-svelte'</c>) where React's and Vue's are default
    ///     exports. The SPA lane encodes it exactly this way for the same reason.
    /// </remarks>
    public string? PluginImport { get; }

    /// <summary>The plugin's call expression for the <c>plugins</c> array.</summary>
    public string? PluginCall { get; }

    /// <summary>
    ///     A custom element registers its own tag and nothing about the file reveals it, so the
    ///     contract is that the module default-exports that name. Importing it also runs the
    ///     registration side effect. Needs no Vite plugin: it is ordinary TypeScript.
    /// </summary>
    public static ExternalRuntime Lit { get; } = new("lit", "tag", "litComponent");

    /// <summary>Covers Preact unchanged, through <c>preact/compat</c> aliasing.</summary>
    public static ExternalRuntime React { get; } =
        new("react", "Component", "reactComponent", "react from '@vitejs/plugin-react'", "react()");

    /// <summary>A single-file component, compiled by a Vite plugin rather than its own compiler.</summary>
    public static ExternalRuntime Vue { get; } =
        new("vue", "Component", "vueComponent", "vue from '@vitejs/plugin-vue'", "vue()");

    /// <summary>A single-file component, compiled by a Vite plugin rather than its own compiler.</summary>
    public static ExternalRuntime Svelte { get; } =
        new(
            "svelte",
            "Component",
            "svelteComponent",
            "{ svelte } from '@sveltejs/vite-plugin-svelte'",
            "svelte()",
            adapterModule: "svelte.svelte");

    /// <summary>
    ///     Every runtime, in the order their plugins are written into the Vite config.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <strong>Single-file compilers first, the JSX transform last.</strong> A Vue or Svelte
    ///         plugin claims one extension it alone understands; the React plugin installs a general
    ///         JSX transform. Ordered the other way round, a <c>.vue</c> reaches the JSX parser and
    ///         fails as <c>Unexpected JSX expression</c> at line 1 — an error naming neither Vue nor
    ///         the plugin that should have handled it.
    ///     </para>
    ///     <para>
    ///         The order is also STABLE, which matters for a second reason: the config is compared
    ///         against what is already on disk and rewritten only when it differs, and an order that
    ///         tracked island discovery would rewrite a bundler input on some builds and not others,
    ///         restarting the dev server's dependency graph for no reason.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<ExternalRuntime> All { get; } = new[] { Vue, Svelte, React, Lit };

    /// <summary>The runtime for a wire key, or null if nothing declares it.</summary>
    public static ExternalRuntime? Find(string? key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

    /// <summary>Every key, for an error message that can name the alternatives.</summary>
    public static string KeyList => string.Join(", ", All.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));
}
