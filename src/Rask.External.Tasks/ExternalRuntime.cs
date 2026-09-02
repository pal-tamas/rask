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
///         "which of seven?", and the one that is missed does not fail — it silently generates a React
///         entry for a Vue component, which builds, ships, loads, and mounts nothing.
///     </para>
///     <para>
///         Modelled on <c>SpaFramework</c> in the CLI, which solved the same problem for the SPA lane:
///         one list, and everything derived from it. The two lanes now cover the same seven front ends.
///     </para>
/// </remarks>
internal sealed class ExternalRuntime
{
    private ExternalRuntime(
        string key,
        string importName,
        string adapterFactory,
        string? pluginImport = null,
        string? pluginFactory = null,
        string? adapterModule = null,
        string[]? extensions = null,
        string? pluginOptions = null)
    {
        Key = key;
        ImportName = importName;
        AdapterFactory = adapterFactory;
        PluginImport = pluginImport;
        PluginFactory = pluginFactory;
        AdapterModule = adapterModule ?? key;
        Extensions = extensions ?? [];
        PluginOptions = pluginOptions;
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

    /// <summary>The identifier the plugin was imported under, called to build the plugin.</summary>
    public string? PluginFactory { get; }

    /// <summary>Options every call carries, whether or not the plugin also needs scoping.</summary>
    /// <remarks>
    ///     Only Angular has any. Its plugin looks for <c>tsconfig.app.json</c> by default and merely
    ///     WARNS when that is missing before going on to build — so leaving it unset is a green build
    ///     with the compiler configured by nothing, and a warning line on every build besides.
    /// </remarks>
    public string? PluginOptions { get; }

    /// <summary>
    ///     The extensions this runtime's plugin has to be CONFINED to when another runtime competes
    ///     for them. Empty for a plugin that must never be confined.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only the three JSX runtimes have any. They install general JSX transforms over the same
    ///         <c>.tsx</c>, so with two of them present each plugin is scoped by directory — measured,
    ///         and measured at the level of the emitted chunk rather than the exit code.
    ///     </para>
    ///     <para>
    ///         <strong>Angular is deliberately absent, though it shares <c>.ts</c> with Lit.</strong>
    ///         It does not need confining: unscoped, the plugin compiles the Angular island ahead of
    ///         time and passes ordinary TypeScript through untouched, so a Lit element sits beside one
    ///         without either losing anything — verified with both in one bundle, by checking the Lit
    ///         chunk still registers its tag and the Angular chunk still carries
    ///         <c>ɵɵdefineComponent</c>. Scoping it would be a rule invented rather than measured.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>
    ///     A custom element registers its own tag and nothing about the file reveals it, so the
    ///     contract is that the module default-exports that name. Importing it also runs the
    ///     registration side effect. Needs no Vite plugin: it is ordinary TypeScript.
    /// </summary>
    public static ExternalRuntime Lit { get; } = new("lit", "tag", "litComponent");

    /// <summary>React, and a Preact project that aliases <c>react</c> to <c>preact/compat</c>.</summary>
    public static ExternalRuntime React { get; } =
        new(
            "react",
            "Component",
            "reactComponent",
            "react from '@vitejs/plugin-react'",
            "react",
            extensions: [".tsx", ".jsx"]);

    /// <summary>
    ///     Preact directly, without <c>preact/compat</c> in the way.
    /// </summary>
    /// <remarks>
    ///     Cannot share a project with <see cref="React" />, and not for a reason Rask chose:
    ///     <c>@vitejs/plugin-react</c> resolves Babel 8 while <c>@preact/preset-vite</c> pins a
    ///     <c>@babel/core@"7.x"</c> peer, so npm refuses to install the two together. Refused by name
    ///     in <see cref="ExternalBuildPlan" /> rather than left to surface as an ERESOLVE tree naming
    ///     neither island.
    /// </remarks>
    public static ExternalRuntime Preact { get; } =
        new(
            "preact",
            "Component",
            "preactComponent",
            "preact from '@preact/preset-vite'",
            "preact",
            extensions: [".tsx", ".jsx"]);

    /// <summary>Solid, whose JSX compiles to DOM operations rather than to a virtual tree.</summary>
    public static ExternalRuntime Solid { get; } =
        new(
            "solid",
            "Component",
            "solidComponent",
            "solid from 'vite-plugin-solid'",
            "solid",
            extensions: [".tsx", ".jsx"]);

    /// <summary>A single-file component, compiled by a Vite plugin rather than its own compiler.</summary>
    public static ExternalRuntime Vue { get; } =
        new(
            "vue",
            "Component",
            "vueComponent",
            "vue from '@vitejs/plugin-vue'",
            "vue");

    /// <summary>A single-file component, compiled by a Vite plugin rather than its own compiler.</summary>
    public static ExternalRuntime Svelte { get; } =
        new(
            "svelte",
            "Component",
            "svelteComponent",
            "{ svelte } from '@sveltejs/vite-plugin-svelte'",
            "svelte",
            adapterModule: "svelte.svelte");

    /// <summary>
    ///     Angular, compiled ahead of time by the Analog plugin.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Shares <c>.ts</c> with Lit and is still never scoped — see <see cref="Extensions" />.
    ///         The plugin compiles the Angular island ahead of time and hands ordinary TypeScript
    ///         through untouched, which is what lets a Lit element sit beside one.
    ///     </para>
    ///     <para>
    ///         What it MUST be told is a tsconfig, and not the app's: that one carries
    ///         <c>"noEmit": true</c>, which makes ngtsc emit nothing and leaves every <c>.ts</c> island
    ///         in the project without a default export — reported as
    ///         <c>"default" is not exported by …</c> against files that plainly have one.
    ///         <see cref="ExternalBuildPlan.AngularTsConfig" /> writes one that exists to emit.
    ///     </para>
    ///     <para>
    ///         The build needs <c>@angular/compiler-cli</c> and <c>@angular/build</c> beside the plugin
    ///         — it imports both and depends on neither — and a TypeScript under 6.1, which
    ///         <c>@angular/compiler-cli</c> pins.
    ///     </para>
    /// </remarks>
    public static ExternalRuntime Angular { get; } =
        new(
            "angular",
            "Component",
            "angularComponent",
            "angular from '@analogjs/vite-plugin-angular'",
            "angular",
            pluginOptions: "jit: false");

    /// <summary>
    ///     Every runtime, in the order their plugins are written into the Vite config.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <strong>Single-file compilers first, the JSX transforms last.</strong> A Vue or Svelte
    ///         plugin claims one extension it alone understands; a React, Preact or Solid plugin
    ///         installs a general JSX transform. Ordered the other way round, a <c>.vue</c> reaches the
    ///         JSX parser and fails as <c>Unexpected JSX expression</c> at line 1 — an error naming
    ///         neither Vue nor the plugin that should have handled it.
    ///     </para>
    ///     <para>
    ///         The order is also STABLE, which matters for a second reason: the config is compared
    ///         against what is already on disk and rewritten only when it differs, and an order that
    ///         tracked island discovery would rewrite a bundler input on some builds and not others,
    ///         restarting the dev server's dependency graph for no reason.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<ExternalRuntime> All { get; } =
        new[] { Vue, Svelte, Angular, Solid, Preact, React, Lit };

    /// <summary>The runtime for a wire key, or null if nothing declares it.</summary>
    public static ExternalRuntime? Find(string? key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

    /// <summary>Every key, for an error message that can name the alternatives.</summary>
    public static string KeyList => string.Join(", ", All.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>
    ///     Whether this runtime and <paramref name="other" /> compete for the same source files AND can
    ///     both be confined to a directory.
    /// </summary>
    /// <remarks>
    ///     A runtime with no <see cref="Extensions" /> never competes here, however much its plugin
    ///     reads: Angular shares <c>.ts</c> with Lit and is left alone because confining it is what
    ///     breaks it.
    /// </remarks>
    public bool SharesExtensionWith(ExternalRuntime other) =>
        !ReferenceEquals(this, other)
        && Extensions.Count > 0
        && Extensions.Any(e => other.Extensions.Contains(e, StringComparer.Ordinal));
}
